using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2CppScheduleOne.PlayerScripts;

namespace ScheduleOneNavigator
{
    // Walks the player to a destination set from elsewhere (FullMapView),
    // re-planning periodically as the player moves. Does not do any
    // click/UI handling itself - see FullMapView for that.
    //
    // History: first used A* Pathfinding Project's ABPath against the
    // game's two exposed graphs ("General Vehicle Graph"/"Road Nodes"),
    // both vehicle-oriented - routes hugged roads because that was the only
    // network available. Then switched to Unity's baked NavMesh (the data
    // NPCs walk on) - better, but that network only covers the specific
    // paths NPCs are scripted to use, so a destination a bit off that
    // network permanently failed pathfinding (confirmed via diagnostic
    // logging: same failure, forever, every 1.5s retry). Now uses our own
    // TerrainGrid - a walkable-area layer baked once from the same physics
    // the player's own CharacterController collides with, so anywhere the
    // player could actually walk is routable, not just NPC-visited paths.
    public class RoutePlanner
    {
        const float DestinationReachedDistance = 3f;
        // A car can't usually pull up within 3m of a customer standing on
        // the pavement, so multi-stop routes stalled on the first stop.
        const float VehicleDestinationReachedDistance = 8f;
        const float RouteRecalcInterval = 1.5f;

        Vector3 destination;
        bool hasDestination;
        float recalcTimer;
        int consecutiveFailures;
        bool lastInVehicle;
        bool debugDumpPending; // first path for each new destination, verbose only
        double lastPathMs;

        readonly List<Vector3> waypoints = new List<Vector3>();
        readonly Queue<(Vector3 position, Func<bool> stillValid, IntPtr key)> queuedStops = new Queue<(Vector3, Func<bool>, IntPtr)>();
        // Optional per-stop check (e.g. "this deal's contract is still
        // active"); a stop that turns invalid is skipped instead of the
        // route waiting forever at a deal that was completed or expired.
        Func<bool> currentStopStillValid;
        IntPtr currentStopKey;

        // Deal route mode (optimal route button): re-planned from current
        // deals + inventory on every recalc tick, and a stop is only left
        // once its deal is actually done - arriving next to the customer
        // isn't a handover, and advancing early made the re-plan send the
        // player straight back to them.
        bool dealRouteActive;
        public bool DealRouteActive => dealRouteActive;

        // Contracts of the current target and all queued stops (deal route).
        public HashSet<IntPtr> ActiveDealKeys
        {
            get
            {
                var keys = new HashSet<IntPtr>();
                if (hasDestination && currentStopKey != IntPtr.Zero)
                    keys.Add(currentStopKey);
                foreach (var stop in queuedStops)
                    if (stop.key != IntPtr.Zero)
                        keys.Add(stop.key);
                return keys;
            }
        }

        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public bool HasRoute => hasDestination && waypoints.Count > 0;
        public bool HasDestination => hasDestination;
        public Vector3 Destination => destination;
        public int QueuedStopsRemaining => queuedStops.Count;

        public IEnumerable<Vector3> QueuedStopPositions
        {
            get
            {
                foreach (var stop in queuedStops)
                    yield return stop.position;
            }
        }

        public void Tick()
        {
            if (!hasDestination)
                return;

            Player player = Player.Local;
            if (player == null)
                return;

            float distToDest = Vector3.Distance(ScheduleOneNavigatorMod.GetTrackedPosition(player), destination);
            float reachedDistance = player.IsInVehicle ? VehicleDestinationReachedDistance : DestinationReachedDistance;
            bool stopObsolete = recalcTimer <= 0f && currentStopStillValid != null && !IsStillValid(currentStopStillValid);
            bool arrived = distToDest <= reachedDistance && currentStopStillValid == null;
            if (stopObsolete && dealRouteActive)
            {
                // Deal delivered/expired: re-plan from here instead of just
                // taking the next queued stop - stock and deals may have
                // changed, and the route shouldn't end while more is servable.
                Log.Debug("[Minimap] Deal route: current deal done/expired - re-planning.");
                hasDestination = false;
                ReplanDealRoute(ScheduleOneNavigatorMod.GetTrackedPosition(player));
                return;
            }
            if (arrived || stopObsolete)
            {
                if (queuedStops.Count > 0)
                {
                    Log.Debug(stopObsolete
                        ? $"[Minimap] Stop no longer valid (deal done/expired), {queuedStops.Count} more queued - advancing."
                        : $"[Minimap] Stop reached, {queuedStops.Count} more queued - advancing.");
                    var next = queuedStops.Dequeue();
                    SetDestinationInternal(next.position, next.stillValid, next.key);
                }
                else
                {
                    Log.Debug("[Minimap] Destination reached, clearing route.");
                    ClearRoute();
                }
                return;
            }

            recalcTimer -= Time.deltaTime;
            if (recalcTimer <= 0f)
            {
                recalcTimer = RouteRecalcInterval;
                if (dealRouteActive && !ReplanDealRoute(ScheduleOneNavigatorMod.GetTrackedPosition(player)))
                    return; // nothing deliverable left, route cleared
                RequestPath(ScheduleOneNavigatorMod.GetTrackedPosition(player), destination);
            }
        }

        // Single-destination navigation (map click, customer marker click).
        // Drops any previously queued multi-stop route - a fresh manual click
        // should always mean "go here instead", not "add another stop".
        public void SetDestination(Vector3 world)
        {
            dealRouteActive = false;
            queuedStops.Clear();
            SetDestinationInternal(world, null, IntPtr.Zero);
        }

        // Returns false if no deal can be served right now.
        public bool StartDealRoute(Vector3 from)
        {
            dealRouteActive = true;
            return ReplanDealRoute(from);
        }

        bool ReplanDealRoute(Vector3 from)
        {
            List<DealInfo> ordered;
            try
            {
                ordered = DealPlanner.PlanDealRoute(from, out _, hasDestination ? currentStopKey : IntPtr.Zero);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Minimap] Deal route re-plan failed: {ex.Message}");
                return true; // keep the current route rather than drop it on a transient error
            }

            if (ordered.Count == 0)
            {
                Log.Debug("[Minimap] Deal route: nothing deliverable with the current inventory - clearing route.");
                ClearRoute();
                return false;
            }

            // Same set of deals still planned -> keep the current order and
            // target, so the route doesn't jump around while walking.
            var planned = new HashSet<IntPtr>();
            foreach (DealInfo d in ordered)
                planned.Add(ContractKey(d));
            var active = new HashSet<IntPtr>();
            if (hasDestination)
                active.Add(currentStopKey);
            foreach (var stop in queuedStops)
                active.Add(stop.key);
            if (hasDestination && planned.SetEquals(active))
                return true;

            Log.Debug($"[Minimap] Deal route re-planned: {ordered.Count} stop(s): " +
                string.Join(" -> ", ordered.ConvertAll(d => d.CustomerName)));
            queuedStops.Clear();
            for (int i = 1; i < ordered.Count; i++)
                queuedStops.Enqueue((ordered[i].DeliveryPosition, StillActive(ordered[i]), ContractKey(ordered[i])));
            if (!hasDestination || currentStopKey != ContractKey(ordered[0]))
                SetDestinationInternal(ordered[0].DeliveryPosition, StillActive(ordered[0]), ContractKey(ordered[0]));
            return true;
        }

        static IntPtr ContractKey(DealInfo d) => d.Contract != null ? d.Contract.Pointer : IntPtr.Zero;

        // True while this exact contract is still the customer's current one
        // (i.e. not yet delivered, expired or cancelled).
        static Func<bool> StillActive(DealInfo d)
        {
            var customer = d.Customer;
            IntPtr key = ContractKey(d);
            return () => customer != null && customer.CurrentContract != null && customer.CurrentContract.Pointer == key;
        }

        static bool IsStillValid(Func<bool> check)
        {
            try { return check(); }
            catch { return true; } // a broken check must not eat the route
        }

        // Multi-stop route (the "optimal route" button): navigates to stops[0]
        // first, then automatically advances through the rest as each one is
        // reached (see Tick()).
        public void SetRoute(IReadOnlyList<Vector3> stops, IReadOnlyList<Func<bool>> stillValid = null)
        {
            queuedStops.Clear();
            if (stops == null || stops.Count == 0)
            {
                ClearRoute();
                return;
            }
            dealRouteActive = false;
            Func<bool> ValidAt(int i) => stillValid != null && i < stillValid.Count ? stillValid[i] : null;
            for (int i = 1; i < stops.Count; i++)
                queuedStops.Enqueue((stops[i], ValidAt(i), IntPtr.Zero));
            SetDestinationInternal(stops[0], ValidAt(0), IntPtr.Zero);
        }

        void SetDestinationInternal(Vector3 world, Func<bool> stillValid, IntPtr key)
        {
            destination = world;
            currentStopStillValid = stillValid;
            currentStopKey = key;
            hasDestination = true;
            debugDumpPending = true;
            recalcTimer = 0f; // trigger an immediate path request on the next Tick
            // Clear stale waypoints from whatever the previous destination was
            // right away, rather than leaving them drawn until the new path
            // finishes computing - a leftover old route pointing the wrong way
            // read as a bug when switching targets (e.g. clicking a new
            // customer while already en route somewhere else).
            waypoints.Clear();
        }

        public void ClearRoute()
        {
            dealRouteActive = false;
            hasDestination = false;
            waypoints.Clear();
            queuedStops.Clear();
        }

        void RequestPath(Vector3 start, Vector3 end)
        {
            Player player = Player.Local;
            bool inVehicle = player != null && player.IsInVehicle;
            if (inVehicle != lastInVehicle)
            {
                lastInVehicle = inVehicle;
                Log.Debug($"[Minimap] Route mode: {(inVehicle ? "vehicle (roads strongly preferred)" : "on foot")}.");
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var raw = TerrainGrid.FindPath(start, end, inVehicle, out string failReason);
            lastPathMs = sw.Elapsed.TotalMilliseconds;
            if (debugDumpPending && Log.Verbose)
            {
                debugDumpPending = false;
                TerrainGrid.DumpDebugGrid(start, end, raw, inVehicle);
            }
            if (raw == null || raw.Count == 0)
            {
                consecutiveFailures++;
                // Stuck destinations fail identically on every 1.5s retry
                // forever (player not moving closer to something unreachable)
                // - only log the 1st/2nd attempt plus every 20th after that,
                // so a long-stuck route doesn't flood the log with hundreds
                // of identical lines while still leaving a trail to confirm
                // it's still stuck.
                if (consecutiveFailures <= 2 || consecutiveFailures % 20 == 0)
                {
                    MelonLogger.Warning($"[Minimap] Pathfinding could not find a route to the clicked point (attempt {consecutiveFailures}). " +
                        $"start={start}, end={end}. Reason: {failReason}");
                }
                return;
            }
            consecutiveFailures = 0;

            waypoints.Clear();
            SimplifyPath(raw, 0, raw.Count - 1, PathSimplifyToleranceMeters, waypoints);
            waypoints.Add(raw[raw.Count - 1]); // recursive simplify only adds the start of each kept segment

            float endOffset = Vector3.Distance(raw[raw.Count - 1], destination);
            Log.Debug($"[Minimap] Path found: {raw.Count} raw cells -> {waypoints.Count} after simplify. " +
                $"Requested dest {destination}, actual path end {raw[raw.Count - 1]}, snap offset {endOffset:F1}m, {lastPathMs:F1}ms.");
        }

        // Was 1.5m - but that could smooth away a real, deliberate bend the
        // raw path took to skirt a building corner, drawing a straight
        // simplified line that visibly clips the corner by up to the
        // tolerance even though the actual collision-checked path never
        // did (looked identical to a real pathfinding corner-cut bug from
        // a screenshot, but the diagonal-edge fix for that didn't help -
        // this was probably the real explanation instead). Tightened to
        // stay within less than half a grid cell of the true path.
        const float PathSimplifyToleranceMeters = 0.75f;

        // TerrainGrid's cell-center waypoints are axis/diagonal-stepped, so
        // an unsimplified path visibly staircases even along an otherwise
        // straight line. Ramer-Douglas-Peucker over the XZ plane collapses
        // runs of near-collinear points into straight segments.
        static void SimplifyPath(List<Vector3> points, int first, int last, float tolerance, List<Vector3> result)
        {
            if (last <= first)
            {
                if (last == first)
                    result.Add(points[first]);
                return;
            }

            Vector3 a = points[first];
            Vector3 b = points[last];
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            Vector2 dir = b2 - a2;
            float len = dir.magnitude;

            float maxDist = -1f;
            int maxIndex = -1;
            for (int i = first + 1; i < last; i++)
            {
                Vector2 p2 = new Vector2(points[i].x, points[i].z);
                float dist = len > 1e-4f
                    ? Mathf.Abs((p2.x - a2.x) * dir.y - (p2.y - a2.y) * dir.x) / len
                    : Vector2.Distance(p2, a2);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIndex = i;
                }
            }

            if (maxIndex != -1 && maxDist > tolerance)
            {
                SimplifyPath(points, first, maxIndex, tolerance, result);
                SimplifyPath(points, maxIndex, last, tolerance, result);
            }
            else
            {
                result.Add(a);
            }
        }
    }
}
