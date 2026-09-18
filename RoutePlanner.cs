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
        const float RouteRecalcInterval = 1.5f;

        Vector3 destination;
        bool hasDestination;
        float recalcTimer;
        int consecutiveFailures;

        readonly List<Vector3> waypoints = new List<Vector3>();
        readonly Queue<Vector3> queuedStops = new Queue<Vector3>();

        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public bool HasRoute => hasDestination && waypoints.Count > 0;
        public bool HasDestination => hasDestination;
        public Vector3 Destination => destination;
        public int QueuedStopsRemaining => queuedStops.Count;

        public void Tick()
        {
            if (!hasDestination)
                return;

            Player player = Player.Local;
            if (player == null)
                return;

            float distToDest = Vector3.Distance(ScheduleOneNavigatorMod.GetTrackedPosition(player), destination);
            if (distToDest <= DestinationReachedDistance)
            {
                if (queuedStops.Count > 0)
                {
                    MelonLogger.Msg($"[Minimap] Stop reached, {queuedStops.Count} more queued - advancing.");
                    SetDestinationInternal(queuedStops.Dequeue());
                }
                else
                {
                    MelonLogger.Msg("[Minimap] Destination reached, clearing route.");
                    ClearRoute();
                }
                return;
            }

            recalcTimer -= Time.deltaTime;
            if (recalcTimer <= 0f)
            {
                recalcTimer = RouteRecalcInterval;
                RequestPath(ScheduleOneNavigatorMod.GetTrackedPosition(player), destination);
            }
        }

        // Single-destination navigation (map click, customer marker click).
        // Drops any previously queued multi-stop route - a fresh manual click
        // should always mean "go here instead", not "add another stop".
        public void SetDestination(Vector3 world)
        {
            queuedStops.Clear();
            SetDestinationInternal(world);
        }

        // Multi-stop route (the "optimal route" button): navigates to stops[0]
        // first, then automatically advances through the rest as each one is
        // reached (see Tick()).
        public void SetRoute(IReadOnlyList<Vector3> stops)
        {
            queuedStops.Clear();
            if (stops == null || stops.Count == 0)
            {
                ClearRoute();
                return;
            }
            for (int i = 1; i < stops.Count; i++)
                queuedStops.Enqueue(stops[i]);
            SetDestinationInternal(stops[0]);
        }

        void SetDestinationInternal(Vector3 world)
        {
            destination = world;
            hasDestination = true;
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
            hasDestination = false;
            waypoints.Clear();
            queuedStops.Clear();
        }

        void RequestPath(Vector3 start, Vector3 end)
        {
            var raw = TerrainGrid.FindPath(start, end, out string failReason);
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
            MelonLogger.Msg($"[Minimap] Path found: {raw.Count} raw cells -> {waypoints.Count} after simplify. " +
                $"Requested dest {destination}, actual path end {raw[raw.Count - 1]}, snap offset {endOffset:F1}m.");
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
