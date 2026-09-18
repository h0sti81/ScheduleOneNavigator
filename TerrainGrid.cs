using System;
using System.Collections.Generic;
using System.Diagnostics;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppPathfinding;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Map;

namespace ScheduleOneNavigator
{
    // Custom walkable-area grid, independent of both the game's two
    // vehicle-only A* graphs (routes hugged roads) and its baked Unity
    // NavMesh (only covers the paths NPCs are scripted to walk, leaving gaps
    // - a destination even a short way off that network permanently fails,
    // see RoutePlanner's earlier "Pathfinding could not find a route"
    // dead-ends). Since the *player* can walk anywhere they're not
    // physically blocked, that's the thing to test directly: every cell is
    // baked once by raycasting for ground and checking for the same
    // collision geometry that blocks the player's own CharacterController,
    // producing our own walkable/unwalkable layer to run A* over.
    //
    // Known limitation (v1, accepted for now): one ground-height raycast per
    // XZ cell makes this a 2.5D heightfield, not true 3D - a raycast from
    // above a multi-storey building hits the roof, not an upstairs floor, so
    // upper floors aren't separately routable. Most delivery/customer points
    // observed so far sit at ground level, so this hasn't come up in
    // practice yet.
    public static class TerrainGrid
    {
        const float CellSize = 2f;
        const float RaycastStartAboveBakeOrigin = 200f; // must clear the tallest roof in town
        const float MaxGroundRayDistance = 600f;
        const float ObstacleCheckHeightMargin = 1.05f; // slight margin over the player's own capsule

        // Bounds are no longer derived from MapPositionUtility.OriginPoint/
        // EdgePoint - that repeated an already-diagnosed bug from the old
        // phone-map click calibration (see RoutePlanner history): those two
        // transforms are NOT guaranteed to be diagonal corners of the map:
        // for this town they turned out to differ by ~2m on X but ~200m on
        // Z, baking a degenerate 1-cell-wide strip that every destination
        // snapped onto (explains the "always the same actual path end"
        // symptom). Instead, bake on demand around whatever start/end a
        // path request actually needs, with margin - sidesteps needing any
        // notion of "the whole map's true extent" at all.
        //
        // A single flat margin (the old BakeMarginMeters=60f) turned out to
        // only be "enough" by luck: it's a fixed pad perpendicular to the
        // straight line between start and end, but the real walkable
        // detour needed to actually connect them (e.g. the one bridge onto
        // an island, or a way around a lake/building cluster on the
        // mainland) doesn't scale with that line at all - it can sit
        // further than 60m off it regardless of how far apart start/end
        // are. That's why routing "worked up to a distance, then failed":
        // for nearby destinations the line happened to pass close enough
        // to the real crossing for 60m to cover it, for farther ones it
        // didn't, even on pure mainland with no island/water involved.
        // FindPath now retries with a wider margin (this ladder) only when
        // the first, cheap attempt leaves start/end disconnected - the
        // common case (already connects on the first try) still bakes
        // exactly the old 60m box and pays no extra cost.
        static readonly float[] BakeMarginAttemptsMeters = { 60f, 120f, 220f };

        // Hard cap on cols*rows for any single bake attempt in the ladder
        // above, checked before paying for it - without this, a
        // genuinely-unbridged destination (a real dead-end island, or two
        // mainland regions with no sampled crossing at all) would keep
        // widening the box every retry with no way to stop, baking an
        // arbitrarily large grid one raycast+capsule-sweep at a time on
        // the main thread. Measured live (2026-09-18 test): ~11us/cell
        // (250m-margin bakes of ~98-100k cells took 1.1-1.2s, all on the
        // main thread inside RoutePlanner.Tick()) - 120,000 cells bounds
        // the worst case to roughly 1.3s. FindPath's boundary-touch check
        // (see ComponentTouchesBoundary) should make hitting this cap rare
        // now - it only matters for a disconnected region that genuinely
        // touches the edge of what's baked so far (a real crossing might
        // exist just outside), not for a region a narrow-crossing/edge-
        // aliasing bug traps entirely inside already-sampled territory
        // (widening can never fix that kind, see FindPath). Retune after
        // seeing how often testing actually reaches this cap.
        const int MaxBakedCells = 120_000;

        static bool baked;
        static float minX, minZ, maxX, maxZ;
        static int cols, rows;
        static bool[] walkable; // [row*cols+col]
        static float[] groundY;
        // True if this cell lies within RoadProximityMeters of the game's
        // own "Road Nodes" A* Pathfinding Project graph (the network built
        // for vehicle routing - see RoutePlanner's history for why we don't
        // use that graph for the player's own pathfinding directly, it's
        // vehicle-oriented and has gaps a pedestrian route can't tolerate).
        // We only borrow it as a proximity signal here, not for
        // connectivity: FindPath gives cells flagged here a cost discount
        // (see RoadCostFactor) so a route prefers a nearby road/sidewalk
        // over a similarly-short cut across open ground, without depending
        // on that graph actually connecting start to end.
        static bool[] isRoad;
        static int roadCellCount;
        const float RoadProximityMeters = 3f;
        // Edge-blocked flags, checked at bake time (see TryBake pass 2) so
        // FindPath itself stays a cheap array lookup. blockedEdgeRight[idx]
        // covers the edge from this cell to its (col+1,row) neighbour,
        // blockedEdgeDown[idx] to its (col,row+1) neighbour - only two
        // directions are stored since the reverse edges are the same edge.
        static bool[] blockedEdgeRight;
        static bool[] blockedEdgeDown;
        // Same idea as blockedEdgeRight/Down, but for the two diagonal
        // directions - see IsDiagonalEdgeBlocked for why these exist
        // separately from the orthogonal ones (corner-cutting through a
        // building whose corner geometry doesn't happen to block either
        // straight edge). blockedEdgeDiagDR[idx] covers (col,row)->
        // (col+1,row+1), blockedEdgeDiagDL[idx] covers (col,row)->
        // (col-1,row+1) - together with mirroring these two cover all four
        // diagonal directions, same trick as the orthogonal pair.
        static bool[] blockedEdgeDiagDR;
        static bool[] blockedEdgeDiagDL;
        static float bakedObstacleRadius;
        static float bakedObstacleHeight;
        static LayerMask bakedGroundMask;
        static float bakedRayStartY;
        static int walkableCellCount;
        // Per-cell reason breakdown for why a cell ended up non-walkable -
        // diagnostic only, logged alongside the bake summary so a
        // suspiciously water-heavy or ground-miss-heavy region shows up
        // without having to guess and add ad-hoc logging after the fact.
        static int noGroundCount;
        static int waterCount;
        static int obstructedCount;
        // Per-cell mirror of the same reason (0=walkable, 1=no-ground,
        // 2=water, 3=obstructed) so a small isolated region can be
        // inspected locally instead of only seeing the whole-bake totals -
        // see LogLocalReasonBreakdown.
        const byte ReasonNoGround = 1, ReasonWater = 2, ReasonObstructed = 3;
        static byte[] unwalkableReason;

        // Map-art water detection (see the water-check comment in Pass 1).
        // Off until LogLocalReasonBreakdown's color calibration data (logged
        // per the island investigation, 2026-09-18) gives real thresholds -
        // these two placeholders are deliberately unset guesses, not tuned
        // values, until then.
        const bool UseColorWaterDetection = false;
        const int MinOpaqueAlpha = 200; // near-transparent/unmapped pixels are "unknown", not "not water"
        const int BlueMarginThreshold = 40; // placeholder - replace from real calibration data before flipping the switch above
        // How far above the raw ground-raycast hit the obstacle capsule's
        // own bottom sits - see TryBake for why this is a multiple of the
        // radius, not a small fixed margin.
        static float bakedFeetClearance;

        static bool CoveredByCurrentBake(Vector3 a, Vector3 b)
        {
            return baked &&
                a.x >= minX && a.x <= maxX && a.z >= minZ && a.z <= maxZ &&
                b.x >= minX && b.x <= maxX && b.z >= minZ && b.z <= maxZ;
        }

        // Cheap (no allocation) estimate of how many cells a bake at this
        // margin would need, so FindPath's retry ladder can skip an
        // attempt that would blow MaxBakedCells without actually paying
        // for it - mirrors the cols/rows math in TryBake below.
        static long ProjectedCellCount(Vector3 a, Vector3 b, float margin)
        {
            long c = Mathf.Max(1, Mathf.CeilToInt((Mathf.Abs(a.x - b.x) + 2f * margin) / CellSize));
            long r = Mathf.Max(1, Mathf.CeilToInt((Mathf.Abs(a.z - b.z) + 2f * margin) / CellSize));
            return c * r;
        }

        // allowReuseExisting is only true for the ladder's first (smallest)
        // margin - CoveredByCurrentBake only checks whether start/end fall
        // geometrically inside the existing box, which is exactly the
        // insufficient check that caused the long-distance bug (see the
        // BakeMarginAttemptsMeters comment above). Every widening retry
        // must force a fresh bake at the larger box regardless of whether
        // the old, smaller one already "covered" both points.
        static bool EnsureBakedAtMargin(Vector3 a, Vector3 b, float margin, bool allowReuseExisting)
        {
            if (allowReuseExisting && CoveredByCurrentBake(a, b))
                return true;

            float lowX = Mathf.Min(a.x, b.x) - margin;
            float highX = Mathf.Max(a.x, b.x) + margin;
            float lowZ = Mathf.Min(a.z, b.z) - margin;
            float highZ = Mathf.Max(a.z, b.z) + margin;

            var sw = Stopwatch.StartNew();
            if (!TryBake(lowX, highX, lowZ, highZ))
            {
                MelonLogger.Warning("[Minimap] TerrainGrid bake failed - missing Player reference. Falling back to no route.");
                return false;
            }

            baked = true;
            walkableCellCount = 0;
            for (int i = 0; i < walkable.Length; i++)
                if (walkable[i]) walkableCellCount++;
            MelonLogger.Msg($"[Minimap] TerrainGrid baked: {cols}x{rows} cells ({CellSize}m each), margin={margin:F0}m, " +
                $"covering X[{minX:F0},{maxX:F0}] Z[{minZ:F0},{maxZ:F0}], " +
                $"{walkableCellCount}/{walkable.Length} walkable ({roadCellCount} near roads), " +
                $"non-walkable breakdown: {noGroundCount} no-ground, {waterCount} water, {obstructedCount} obstructed, " +
                $"{sw.ElapsedMilliseconds}ms.");
            return true;
        }

        static bool TryBake(float lowX, float highX, float lowZ, float highZ)
        {
            Player player = Player.Local;
            if (player == null || !PlayerMovement.InstanceExists)
                return false;

            float radius = 0.4f;
            float height = 1.8f;
            CharacterController controller = PlayerMovement.Instance.Controller;
            if (controller != null)
            {
                radius = controller.radius;
                height = controller.height;
            }
            bakedObstacleRadius = radius;
            bakedObstacleHeight = height * ObstacleCheckHeightMargin;
            bakedGroundMask = PlayerMovement.Instance.GroundDetectionMask;

            // The obstacle capsule's bottom needs to clear the ground surface
            // itself wherever it's sloped, or the slope pokes into the
            // capsule's own rounded bottom and reads as an obstacle (this
            // was the actual cause behind both the "hillside beside the
            // staircase is unwalkable" bug and, separately, an attempted fix
            // via excluding the ground layer/collider from the obstacle
            // check by identity - which turned out fragile: real buildings
            // sometimes share a layer or even a collider with the ground,
            // so identity-based exclusion also hid their walls, letting
            // routes cut straight through them. Geometrically, a slope at
            // angle theta needs vertical clearance >= radius / cos(theta) to
            // never touch the capsule's rounded cap. FeetClearanceFactor=2
            // covers slopes up to 60 degrees (cos 60 deg = 0.5) - comfortably
            // beyond anything actually walkable in this game - while a true
            // wall (near-90 degrees) still towers well past this height, so
            // it's still caught with a plain, unrestricted `~0` mask and no
            // per-collider bookkeeping at all.
            const float FeetClearanceFactor = 2f;
            bakedFeetClearance = bakedObstacleRadius * FeetClearanceFactor;

            minX = lowX;
            minZ = lowZ;
            maxX = highX;
            maxZ = highZ;

            cols = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / CellSize));
            rows = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / CellSize));

            walkable = new bool[cols * rows];
            unwalkableReason = new byte[cols * rows];
            groundY = new float[cols * rows];
            isRoad = new bool[cols * rows];
            blockedEdgeRight = new bool[cols * rows];
            blockedEdgeDown = new bool[cols * rows];
            blockedEdgeDiagDR = new bool[cols * rows];
            blockedEdgeDiagDL = new bool[cols * rows];

            float rayStartY = player.transform.position.y + RaycastStartAboveBakeOrigin;
            bakedRayStartY = rayStartY;

            // Built once per bake, reused for every cell's road-proximity
            // check below - constrains AstarPath.GetNearest to only ever
            // return nodes from the game's own "Road Nodes" graph.
            // AstarPath.active can be null very early in a fresh scene
            // load; road-preference is a nice-to-have, so we degrade to
            // "no road preference this bake" rather than failing the whole
            // bake over it.
            NNConstraint roadConstraint = null;
            if (AstarPath.active != null)
            {
                roadConstraint = new NNConstraint
                {
                    graphMask = GraphMask.FromGraphName("Road Nodes"),
                    distanceXZ = true
                };
            }
            roadCellCount = 0;
            noGroundCount = 0;
            waterCount = 0;
            obstructedCount = 0;

            // Pass 1: per-cell ground height, obstacle and water check.
            for (int row = 0; row < rows; row++)
            {
                float z = minZ + (row + 0.5f) * CellSize;
                for (int col = 0; col < cols; col++)
                {
                    float x = minX + (col + 0.5f) * CellSize;
                    int idx = row * cols + col;

                    Vector3 rayStart = new Vector3(x, rayStartY, z);
                    if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, MaxGroundRayDistance, bakedGroundMask, QueryTriggerInteraction.Ignore))
                    {
                        walkable[idx] = false;
                        unwalkableReason[idx] = ReasonNoGround;
                        noGroundCount++;
                        continue;
                    }

                    // Water detection. Two signals, switched by
                    // UseColorWaterDetection:
                    //
                    // (a) Physics: water sits on a trigger collider
                    // (WaterCollider - warps the player/vehicle back out on
                    // contact) floating above the lakebed's own solid ground
                    // collider. The ground raycast above ignores triggers, so
                    // it still finds "ground" under water. A second,
                    // trigger-aware raycast up to that same ground distance
                    // catches the water surface itself first. Suspected bug
                    // (island test, 2026-09-18): this trigger volume may
                    // extend past the visible water surface and wrongly mark
                    // real land/road cells near a shoreline as water - see
                    // the local reason breakdown below.
                    //
                    // (b) Map art: the game's own phone-map image is the same
                    // visual truth a player already navigates by. Sampling
                    // its color at this cell sidesteps trusting an opaque
                    // trigger volume entirely (user's proposal, 2026-09-18).
                    // Kept behind a kill-switch and only used once real
                    // calibration data (see the color stats logged in
                    // LogLocalReasonBreakdown) has set real thresholds below
                    // - not guessed. Falls back to (a) whenever the map
                    // texture isn't loaded yet (e.g. very early scene load),
                    // so this never silently treats a cell as "not water"
                    // just because the sampler wasn't ready.
                    bool isWater;
                    if (UseColorWaterDetection && MapColorSampler.TryGetColorAt(hit.point, out Color32 mapColor))
                    {
                        isWater = mapColor.a >= MinOpaqueAlpha &&
                            (mapColor.b - Mathf.Max(mapColor.r, mapColor.g)) >= BlueMarginThreshold;
                    }
                    else
                    {
                        isWater = Physics.Raycast(rayStart, Vector3.down, out RaycastHit waterHit, hit.distance, ~0, QueryTriggerInteraction.Collide) &&
                            waterHit.collider.GetComponentInParent<WaterCollider>() != null;
                    }
                    if (isWater)
                    {
                        walkable[idx] = false;
                        unwalkableReason[idx] = ReasonWater;
                        waterCount++;
                        continue;
                    }

                    groundY[idx] = hit.point.y;

                    Vector3 feet = hit.point + Vector3.up * bakedFeetClearance;
                    Vector3 head = feet + Vector3.up * (bakedObstacleHeight - bakedObstacleRadius * 2f);
                    walkable[idx] = !IsObstructed(feet, head);
                    if (!walkable[idx])
                    {
                        unwalkableReason[idx] = ReasonObstructed;
                        obstructedCount++;
                        continue;
                    }

                    if (roadConstraint != null)
                    {
                        NNInfo nn = AstarPath.active.GetNearest(hit.point, roadConstraint);
                        if (nn.node != null)
                        {
                            float dx = nn.clampedPosition.x - x;
                            float dz = nn.clampedPosition.z - z;
                            if (dx * dx + dz * dz <= RoadProximityMeters * RoadProximityMeters)
                            {
                                isRoad[idx] = true;
                                roadCellCount++;
                            }
                        }
                    }
                }
            }

            // Pass 2: edge sweeps between adjacent walkable cells. A single
            // per-cell obstacle check (pass 1) can miss a thin wall that
            // happens to fall entirely between two 2m-apart cell centres -
            // both flanking samples land in open floor space, so the grid
            // reads as a clear, continuous corridor straight through the
            // wall (the "route cut through a house" symptom from the first
            // live test). Sweeping a capsule along each edge catches
            // anything solid in between, not just at the sample points.
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int idx = row * cols + col;
                    if (!walkable[idx])
                        continue;

                    if (col + 1 < cols && walkable[idx + 1])
                        blockedEdgeRight[idx] = EdgeBlocked(CellFeet(col, row), CellFeet(col + 1, row));
                    if (row + 1 < rows && walkable[idx + cols])
                        blockedEdgeDown[idx] = EdgeBlocked(CellFeet(col, row), CellFeet(col, row + 1));
                    // Diagonal sweeps checked directly along the diagonal
                    // itself, not just approximated from the two flanking
                    // orthogonal edges - a building corner can sit such
                    // that neither straight edge is blocked, yet the
                    // diagonal line still clips it (classic grid-pathfinding
                    // corner-cutting, seen live cutting through a house
                    // corner despite the existing orthogonal-only check).
                    if (col + 1 < cols && row + 1 < rows && walkable[idx + cols + 1])
                        blockedEdgeDiagDR[idx] = EdgeBlocked(CellFeet(col, row), CellFeet(col + 1, row + 1));
                    if (col - 1 >= 0 && row + 1 < rows && walkable[idx + cols - 1])
                        blockedEdgeDiagDL[idx] = EdgeBlocked(CellFeet(col, row), CellFeet(col - 1, row + 1));
                }
            }

            return true;
        }

        static Vector3 CellFeet(int col, int row) => CellWorldPos(col, row) + Vector3.up * bakedFeetClearance;

        // Real building entrances often sit behind a few steps or a short
        // ramp. A single straight sweep directly between two cells' own
        // (different) ground heights dips below that riser and registers a
        // false hit, even though a player just walks up it normally - this
        // was the likely explanation for pathfinding failures where the
        // *player's own* start position came back as a tiny isolated island
        // fully surrounded by "blocked" edges (see session notes: Test
        // 21/22). Rather than widening the whole grid's resolution just for
        // the rare edges that actually climb something (most of a town is
        // flat), only edges with a meaningful height change get re-sampled
        // at several sub-points with their own ground raycasts - the flat
        // majority still costs a single cheap sweep. A real staircase then
        // shows up as several short, gentle sub-steps that clear fine; a
        // genuine unclimbable drop (cliff/retaining wall) still gets caught
        // because at least one of those short sub-sweeps has to cross the
        // actual vertical face between two very differently-grounded
        // sub-points.
        const float SlopeRefineHeightThreshold = 0.3f;
        const int EdgeRefineSubsteps = 4; // ~0.5m spacing across a 2m edge

        static bool EdgeBlocked(Vector3 feetA, Vector3 feetB)
        {
            float heightDiff = Mathf.Abs(feetB.y - feetA.y);
            if (heightDiff <= SlopeRefineHeightThreshold)
            {
                float sweepY = Mathf.Max(feetA.y, feetB.y);
                return SegmentBlocked(
                    new Vector3(feetA.x, sweepY, feetA.z),
                    new Vector3(feetB.x, sweepY, feetB.z));
            }

            Vector3 prev = feetA;
            for (int i = 1; i <= EdgeRefineSubsteps; i++)
            {
                float t = (float)i / EdgeRefineSubsteps;
                float x = Mathf.Lerp(feetA.x, feetB.x, t);
                float z = Mathf.Lerp(feetA.z, feetB.z, t);
                Vector3 cur = ReGround(x, z, prev);

                if (SegmentBlocked(prev, cur))
                    return true;

                prev = cur;
            }
            return false;
        }

        // Re-raycasts for ground at one sub-point during edge refinement
        // (see EdgeBlocked), instead of linearly interpolating between the
        // edge's two endpoint heights - that's the whole point, it's what
        // lets a staircase's actual step profile show up instead of a
        // straight line through it. Falls back to holding the previous
        // sub-point's height if no ground is found here (e.g. the ray
        // grazes past the lip of a step) and lets the capsule sweep's own
        // obstacle check be the judge instead of guessing.
        static Vector3 ReGround(float x, float z, Vector3 fallback)
        {
            Vector3 rayStart = new Vector3(x, bakedRayStartY, z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, MaxGroundRayDistance, bakedGroundMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * bakedFeetClearance;
            return new Vector3(x, fallback.y, z);
        }

        // Standalone ground-height lookup, independent of any existing bake
        // (unlike ReGround above, which needs bakedRayStartY/bakedGroundMask
        // from a bake that's already happened) - used by FullMapView to find
        // the real elevation at a clicked map point before it ever becomes a
        // TerrainGrid start/end. A map click has no real 3D-scene raycast of
        // its own to fall back on (the map is a flat image, not a world
        // click), so without this the caller has nothing better than the
        // player's own current height to guess with - wrong for any click
        // whose real elevation differs from the player's by more than
        // FindPath's MaxSnapHeightDiff, which is increasingly likely the
        // farther away the click is.
        public static bool TryGetGroundHeight(float x, float z, out float groundY)
        {
            groundY = 0f;
            Player player = Player.Local;
            if (player == null || !PlayerMovement.InstanceExists)
                return false;

            float rayStartY = player.transform.position.y + RaycastStartAboveBakeOrigin;
            Vector3 rayStart = new Vector3(x, rayStartY, z);
            if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, MaxGroundRayDistance,
                    PlayerMovement.Instance.GroundDetectionMask, QueryTriggerInteraction.Ignore))
                return false;

            groundY = hit.point.y;
            return true;
        }

        static bool SegmentBlocked(Vector3 a, Vector3 b)
        {
            Vector3 head = a + Vector3.up * (bakedObstacleHeight - bakedObstacleRadius * 2f);
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist < 1e-4f)
                return IsObstructed(a, head);
            Vector3 dir = delta / dist;
            return Physics.CapsuleCast(a, head, bakedObstacleRadius, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        }

        static bool IsObstructed(Vector3 feet, Vector3 head) =>
            Physics.CheckCapsule(feet, head, bakedObstacleRadius, ~0, QueryTriggerInteraction.Ignore);

        // True if the straight edge from (col,row) in direction (dc,dr)
        // (dc,dr one of the 4 orthogonal unit steps) was found blocked
        // during bake - see Pass 2 above.
        static bool IsOrthogonalEdgeBlocked(int col, int row, int dc, int dr)
        {
            if (dr == 0)
            {
                int leftCol = dc > 0 ? col : col + dc;
                return blockedEdgeRight[row * cols + leftCol];
            }
            int topRow = dr > 0 ? row : row + dr;
            return blockedEdgeDown[topRow * cols + col];
        }

        // True if the diagonal edge from (col,row) in direction (dc,dr)
        // (dc,dr one of the 4 diagonal unit steps) was found blocked during
        // bake - see Pass 2 above. Mirroring works the same way as
        // IsOrthogonalEdgeBlocked: each stored array covers one diagonal
        // "orientation" (DR: top-left-to-bottom-right corner pair, DL:
        // top-right-to-bottom-left), and both directions along that
        // orientation resolve to the same stored edge by normalizing to
        // whichever endpoint has the smaller column.
        static bool IsDiagonalEdgeBlocked(int col, int row, int dc, int dr)
        {
            if (dc == dr)
            {
                int baseCol = dc > 0 ? col : col + dc;
                int baseRow = dr > 0 ? row : row + dr;
                return blockedEdgeDiagDR[baseRow * cols + baseCol];
            }
            int dlCol = dc < 0 ? col : col + dc;
            int dlRow = dr > 0 ? row : row + dr;
            return blockedEdgeDiagDL[dlRow * cols + dlCol];
        }

        static int ColOf(float x) => Mathf.Clamp(Mathf.FloorToInt((x - minX) / CellSize), 0, cols - 1);
        static int RowOf(float z) => Mathf.Clamp(Mathf.FloorToInt((z - minZ) / CellSize), 0, rows - 1);
        static Vector3 CellWorldPos(int col, int row) => new Vector3(minX + (col + 0.5f) * CellSize, groundY[row * cols + col], minZ + (row + 0.5f) * CellSize);

        // A baked cell only records ONE ground height per XZ column (see the
        // 2.5D-heightfield limitation noted at the top of this file) - stand
        // in an upper-floor room reached by an exterior staircase and the
        // raycast for that column hits the roof above you, not your actual
        // floor. If the roof happens to read as unobstructed, that cell is
        // "walkable" too, just tens of metres above where the player really
        // is - and being right there, it used to win the snap search
        // immediately, silently routing from the isolated rooftop instead
        // of from the real position. Requiring the candidate's baked ground
        // height to be within reach of where the point actually asked for
        // makes the search skip that roof cell and keep spiraling outward
        // until it finds one at a matching floor - typically the landing or
        // ground at the foot of the stairs.
        const float MaxSnapHeightDiff = 2.5f;

        static bool MatchesHeight(int idx, float expectedY) =>
            walkable[idx] && Mathf.Abs(groundY[idx] - expectedY) <= MaxSnapHeightDiff;

        // Nearest walkable cell (at roughly the right height) to a given
        // one, spiraling outward - used when the requested start/end itself
        // lands on a blocked/off-grid/wrong-floor cell (e.g. clicked a
        // point slightly inside a wall's footprint, or stands on a
        // different storey than our single-height-per-column bake sees).
        static bool TryFindNearestWalkable(int col, int row, float expectedY, int maxRadius, out int outCol, out int outRow)
        {
            if (col >= 0 && col < cols && row >= 0 && row < rows && MatchesHeight(row * cols + col, expectedY))
            {
                outCol = col;
                outRow = row;
                return true;
            }

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dc = -r; dc <= r; dc++)
                {
                    for (int dr = -r; dr <= r; dr++)
                    {
                        if (Mathf.Max(Mathf.Abs(dc), Mathf.Abs(dr)) != r)
                            continue; // only the ring at exactly this radius
                        int c = col + dc;
                        int rr = row + dr;
                        if (c < 0 || c >= cols || rr < 0 || rr >= rows)
                            continue;
                        if (MatchesHeight(rr * cols + c, expectedY))
                        {
                            outCol = c;
                            outRow = rr;
                            return true;
                        }
                    }
                }
            }

            outCol = -1;
            outRow = -1;
            return false;
        }

        const int SnapSearchRadiusCells = 15; // 15*CellSize = ~30m

        // Makes stepping onto a road/sidewalk cell 40% cheaper than the same
        // step across open ground - enough to visibly pull a route onto a
        // nearby road instead of cutting a diagonal shortcut across a lawn,
        // without being so strong that a genuinely far detour via road wins
        // over a short, obviously-more-direct off-road stretch. Makes the
        // heuristic technically inadmissible (real cost can now be lower
        // than straight-line distance), which can make A* explore a few
        // more nodes before settling - a non-issue at this town's grid
        // sizes, and the existing Ramer-Douglas-Peucker simplification in
        // RoutePlanner smooths over the minor path-optimality tradeoff.
        const float RoadCostFactor = 0.6f;

        // Extracted from FindPath so the retry ladder below can run the
        // same search again against a wider bake without duplicating it.
        // Standard grid A*, 8-directional, straight-line heuristic. Grid
        // sizes here (a small town at a few metres per cell) keep explored
        // node counts low enough that a plain List-based open set is fine -
        // no need for a binary heap. Always returns `closed` (start's
        // connected component, for free - see the caller's fallback logic)
        // even on failure.
        static List<Vector3> TryAStar(int startIdx, int endIdx, int endCol, int endRow, out HashSet<int> closed)
        {
            var gScore = new Dictionary<int, float>();
            var cameFrom = new Dictionary<int, int>();
            var open = new List<int>();
            var openSet = new HashSet<int>();
            closed = new HashSet<int>();

            gScore[startIdx] = 0f;
            open.Add(startIdx);
            openSet.Add(startIdx);

            float HeuristicCells(int idx)
            {
                int c = idx % cols, r = idx / cols;
                int dc = c - endCol, dr = r - endRow;
                return Mathf.Sqrt(dc * dc + dr * dr);
            }

            int guard = cols * rows + 16; // hard cap so a bug can't hang the frame forever
            while (open.Count > 0 && guard-- > 0)
            {
                int bestPos = 0;
                float bestScore = gScore[open[0]] + HeuristicCells(open[0]);
                for (int i = 1; i < open.Count; i++)
                {
                    float score = gScore[open[i]] + HeuristicCells(open[i]);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPos = i;
                    }
                }

                int current = open[bestPos];
                open.RemoveAt(bestPos);
                openSet.Remove(current);

                if (current == endIdx)
                    return ReconstructPath(cameFrom, current);

                closed.Add(current);

                int curCol = current % cols, curRow = current / cols;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dc == 0 && dr == 0)
                            continue;
                        int nc = curCol + dc, nr = curRow + dr;
                        if (nc < 0 || nc >= cols || nr < 0 || nr >= rows)
                            continue;
                        int nIdx = nr * cols + nc;
                        if (!walkable[nIdx] || closed.Contains(nIdx))
                            continue;
                        if (dc != 0 && dr != 0)
                        {
                            // Block diagonal cuts through a corner where
                            // either orthogonal neighbour is unwalkable, or
                            // either straight leg's own edge sweep found an
                            // obstacle - avoids routes clipping through the
                            // corner (or a thin diagonal wall) of a
                            // building. Also directly sweep the diagonal
                            // itself (IsDiagonalEdgeBlocked): a corner can
                            // sit such that neither straight edge is
                            // blocked, yet the diagonal line still clips it
                            // - confirmed live (route cut through a house
                            // corner despite the orthogonal-only check).
                            int orthA = curRow * cols + nc;
                            int orthB = nr * cols + curCol;
                            if (!walkable[orthA] || !walkable[orthB])
                                continue;
                            if (IsOrthogonalEdgeBlocked(curCol, curRow, dc, 0) || IsOrthogonalEdgeBlocked(curCol, curRow, 0, dr))
                                continue;
                            if (IsDiagonalEdgeBlocked(curCol, curRow, dc, dr))
                                continue;
                        }
                        else if (IsOrthogonalEdgeBlocked(curCol, curRow, dc, dr))
                        {
                            continue;
                        }

                        float stepCost = (dc != 0 && dr != 0) ? 1.41421356f : 1f;
                        // Roads/sidewalks cost less to cross than open ground
                        // of the same length - see the `isRoad` field comment.
                        // Discounting only the destination cell of a step
                        // (not both endpoints) is a simplification, but it's
                        // enough to make a route noticeably prefer hugging a
                        // road over a similarly-short diagonal shortcut
                        // across a lawn, which is all this is meant to do.
                        if (isRoad[nIdx])
                            stepCost *= RoadCostFactor;
                        float tentativeG = gScore[current] + stepCost;
                        if (!gScore.TryGetValue(nIdx, out float existingG) || tentativeG < existingG)
                        {
                            gScore[nIdx] = tentativeG;
                            cameFrom[nIdx] = current;
                            if (!openSet.Contains(nIdx))
                            {
                                open.Add(nIdx);
                                openSet.Add(nIdx);
                            }
                        }
                    }
                }
            }

            return null;
        }

        // Single-slot memo of "this exact destination was unreachable even
        // at the widest bake margin we're willing to try" - RoutePlanner.
        // Tick() calls FindPath every 1.5s for as long as a route is
        // active, and a genuinely disconnected destination (e.g. a real
        // dead-end island) would otherwise re-run the whole widening ladder
        // below every single tick forever. One slot is enough since this
        // mod only ever tracks one active route at a time - no need for a
        // general cache. Cleared as soon as any attempt actually connects,
        // so it never masks a route that becomes findable again (player
        // walks somewhere new, or the destination changes).
        static bool hasUnreachableCache;
        static Vector3 unreachableCacheDestination;
        static Vector3 unreachableCacheStartAtComputation;
        static float unreachableCacheExpiryTime;
        const float UnreachableCacheStartMoveInvalidationMeters = 15f; // roughly a tick or two of player movement
        const float UnreachableCacheExpirySeconds = 30f; // cheap self-heal net, not the primary invalidation path

        static bool TryUseUnreachableCache(Vector3 start, Vector3 end, out List<Vector3> fallbackPath)
        {
            fallbackPath = null;
            if (!hasUnreachableCache) return false;
            if (unreachableCacheDestination != end) return false;
            if (Time.time >= unreachableCacheExpiryTime) return false;
            if (Vector3.Distance(start, unreachableCacheStartAtComputation) > UnreachableCacheStartMoveInvalidationMeters) return false;
            if (!CoveredByCurrentBake(start, end)) return false; // player left the last baked box entirely

            // Re-derive the cheap "walk to nearest reachable point" stub
            // over the already-resident (widest-tried) grid - no TryBake
            // call at all, just one BFS flood fill.
            if (!TryFindNearestWalkable(ColOf(end.x), RowOf(end.z), end.y, SnapSearchRadiusCells, out int endCol, out int endRow))
                return false; // defensive; shouldn't happen since this grid already covered end when the cache was set

            fallbackPath = FindNearestReachablePathToDestination(endRow * cols + endCol, start, out _, out _);
            return fallbackPath != null;
        }

        static void CacheUnreachable(Vector3 start, Vector3 end)
        {
            hasUnreachableCache = true;
            unreachableCacheDestination = end;
            unreachableCacheStartAtComputation = start;
            unreachableCacheExpiryTime = Time.time + UnreachableCacheExpirySeconds;
        }

        public static List<Vector3> FindPath(Vector3 start, Vector3 end, out string failReason)
        {
            failReason = null;

            if (TryUseUnreachableCache(start, end, out List<Vector3> cachedFallback))
                return cachedFallback;

            HashSet<int> lastClosed = null, lastEndComponent = null;
            List<Vector3> lastFallbackPath = null;
            int startIdx = -1, endIdx = -1, lastEndComponentSize = 0;
            float startSnapDist = 0f, endSnapDist = 0f;

            for (int attempt = 0; attempt < BakeMarginAttemptsMeters.Length; attempt++)
            {
                float margin = BakeMarginAttemptsMeters[attempt];
                // Widening further would blow the cell cap - stop here and
                // use whatever the last (smaller, already-baked) attempt
                // found, rather than paying for a bake we'd have to accept
                // as a failure anyway. Attempt 0 always runs regardless of
                // this check - realistic in-game distances never come
                // close to the cap at the base 60m margin.
                if (attempt > 0 && ProjectedCellCount(start, end, margin) > MaxBakedCells)
                    break;

                if (!EnsureBakedAtMargin(start, end, margin, allowReuseExisting: attempt == 0))
                {
                    failReason = "bake failed (no Player/PlayerMovement reference yet)";
                    return null;
                }

                int rawStartCol = ColOf(start.x), rawStartRow = RowOf(start.z);
                int rawEndCol = ColOf(end.x), rawEndRow = RowOf(end.z);

                if (!TryFindNearestWalkable(rawStartCol, rawStartRow, start.y, SnapSearchRadiusCells, out int startCol, out int startRow))
                {
                    failReason = $"start has no walkable cell within {SnapSearchRadiusCells * CellSize:F0}m matching its height " +
                        $"(raw cell col={rawStartCol},row={rawStartRow} of {cols}x{rows}, expectedY={start.y:F1})";
                    return null;
                }
                if (!TryFindNearestWalkable(rawEndCol, rawEndRow, end.y, SnapSearchRadiusCells, out int endCol, out int endRow))
                {
                    failReason = $"end has no walkable cell within {SnapSearchRadiusCells * CellSize:F0}m matching its height " +
                        $"(raw cell col={rawEndCol},row={rawEndRow} of {cols}x{rows}, expectedY={end.y:F1})";
                    return null;
                }

                startSnapDist = Vector3.Distance(start, CellWorldPos(startCol, startRow));
                endSnapDist = Vector3.Distance(end, CellWorldPos(endCol, endRow));

                startIdx = startRow * cols + startCol;
                endIdx = endRow * cols + endCol;
                if (startIdx == endIdx)
                {
                    hasUnreachableCache = false;
                    return new List<Vector3> { CellWorldPos(endCol, endRow) };
                }

                var path = TryAStar(startIdx, endIdx, endCol, endRow, out HashSet<int> closed);
                if (path != null)
                {
                    hasUnreachableCache = false;
                    return path;
                }

                // Disconnected at this margin. `closed` IS start's connected
                // component for free (A* run to exhaustion necessarily
                // visited every cell reachable from startIdx) - only end's
                // side needs a fresh flood fill, which
                // FindNearestReachablePathToDestination does while also
                // tracking the nearest cell to `start` (used as the "walk
                // to nearest reachable point" fallback below). Computed on
                // every failed attempt, not just the last one, so it can
                // feed the escalation check right below - it's a plain
                // in-memory BFS over already-baked arrays, no physics
                // calls, cheap next to a bake.
                lastClosed = closed;
                lastFallbackPath = FindNearestReachablePathToDestination(endIdx, start, out lastEndComponentSize, out lastEndComponent);

                // If EITHER side's component fails to touch the outer edge
                // of what's baked so far, that side is already fully
                // determined (see ComponentTouchesBoundary): every one of
                // its members has all 8 neighbours already accounted for
                // inside the current box, so it is already exactly as large
                // as it will ever be, at ANY wider margin. Since start and
                // end are (by construction, having reached this line) NOT
                // in the same component yet, one of them being permanently
                // final proves the two can never merge - it doesn't matter
                // whether the OTHER side happens to be huge and touching
                // the boundary itself (e.g. start's component being "the
                // whole mainland network, obviously touching the box edge"
                // says nothing about whether the tiny, fully-interior
                // destination pocket could ever gain new neighbours - it
                // can't). This must be OR, not AND: first shipped as AND
                // and verified live (2026-09-18) to essentially never fire,
                // since a large well-connected side almost always touches
                // the boundary somewhere even when the real disconnect
                // (e.g. a 2-16 cell edge-aliasing pocket on the other side)
                // is fully interior and un-growable - the ladder kept
                // climbing all the way to 220m for nothing, adding the new
                // per-attempt BFS cost on top with no offsetting benefit.
                bool moreRungsAvailable = attempt < BakeMarginAttemptsMeters.Length - 1;
                if (moreRungsAvailable && (!ComponentTouchesBoundary(lastClosed) || !ComponentTouchesBoundary(lastEndComponent)))
                    break;
            }

            // Either the ladder is exhausted/capped, or the check above
            // determined widening further couldn't help: the two sides
            // really are disconnected within however far we were willing to
            // look. Rather than just failing outright, fall back to routing
            // from whichever cell in the DESTINATION's own reachable region
            // sits closest to the player's real position - typically the
            // pavement right outside - so a route preview still shows up
            // even while stuck somewhere unroutable, instead of nothing
            // (user request after Test 22: "das Routing könnte ... an der
            // nächsten begehbaren Position starten").
            CacheUnreachable(start, end);
            if (lastFallbackPath != null && lastFallbackPath.Count > 1)
            {
                LogFallbackOnce(startIdx, endIdx, lastClosed.Count, lastEndComponentSize);
                if (lastEndComponentSize <= SmallComponentLogThreshold)
                {
                    LogLocalReasonBreakdown(endIdx, "end");
                    LogComponentBoundary(lastEndComponent, "end");
                }
                if (lastClosed.Count <= SmallComponentLogThreshold)
                    LogComponentBoundary(lastClosed, "start");
                return lastFallbackPath;
            }

            failReason = $"start/end snapped OK (snap dist {startSnapDist:F1}m/{endSnapDist:F1}m) but no connected " +
                $"path exists between them in the grid even after widening the bake margin up to {BakeMarginAttemptsMeters[BakeMarginAttemptsMeters.Length - 1]:F0}m - " +
                $"start's connected region has {lastClosed.Count} cells, end's has " +
                $"{lastEndComponentSize} cells (grid has {walkableCellCount} walkable of {walkable.Length} total) - " +
                $"regions are disconnected (wall/fence/water gap with no sampled crossing)";
            if (lastEndComponentSize <= SmallComponentLogThreshold)
            {
                LogLocalReasonBreakdown(endIdx, "end");
                LogComponentBoundary(lastEndComponent, "end");
            }
            if (lastClosed.Count <= SmallComponentLogThreshold)
            {
                LogLocalReasonBreakdown(startIdx, "start");
                LogComponentBoundary(lastClosed, "start");
            }
            return null;
        }

        static int lastFallbackStartIdx = -1, lastFallbackEndIdx = -1;

        static void LogFallbackOnce(int startIdx, int endIdx, int startComponentSize, int endComponentSize)
        {
            if (startIdx == lastFallbackStartIdx && endIdx == lastFallbackEndIdx)
                return;
            lastFallbackStartIdx = startIdx;
            lastFallbackEndIdx = endIdx;
            MelonLogger.Msg($"[Minimap] TerrainGrid: no direct path from player (isolated region {startComponentSize} " +
                $"cells) - falling back to nearest reachable point in destination's region ({endComponentSize} cells).");
        }

        // Below this, a connected region is small enough that "why is
        // almost everything around here non-walkable" is worth a direct
        // answer instead of another round of guessing - see the
        // reachability breakdown discussion after Test 22 (user reported
        // clicking known-walkable/road cells on the island and still
        // getting a tiny disconnected region back).
        const int SmallComponentLogThreshold = 20;
        const int LocalBreakdownRadiusCells = 6; // ~12m box around the cell

        static int lastBreakdownIdx = -1;

        // Counts unwalkableReason values in a box around `idx` and logs the
        // split - cheap (a few hundred array reads at most) and only runs
        // on the rare small-component path, so no perf concern baking this
        // in permanently rather than bolting it on after another blind fix
        // attempt fails.
        static void LogLocalReasonBreakdown(int idx, string label)
        {
            if (idx == lastBreakdownIdx)
                return;
            lastBreakdownIdx = idx;

            int centerCol = idx % cols, centerRow = idx / cols;
            int noGround = 0, water = 0, obstructed = 0, ok = 0, outOfBounds = 0;

            // Color calibration (2026-09-18, island investigation): samples
            // the map-art color at every cell in this box, bucketed by the
            // reason the physics-based check already assigned it, so a real
            // BlueMarginThreshold can be read off a live log instead of
            // guessed - see UseColorWaterDetection. The "water" bucket here
            // is labeled by the very system under suspicion, so min/max is
            // logged, not just an average: a bimodal split (some samples
            // clearly blue, others matching the walkable bucket) would be
            // independent confirmation that the trigger check is wrongly
            // eating real land, using a completely different data source.
            var waterColor = new ColorStats();
            var walkableColor = new ColorStats();

            for (int dr = -LocalBreakdownRadiusCells; dr <= LocalBreakdownRadiusCells; dr++)
            {
                int r = centerRow + dr;
                for (int dc = -LocalBreakdownRadiusCells; dc <= LocalBreakdownRadiusCells; dc++)
                {
                    int c = centerCol + dc;
                    if (c < 0 || c >= cols || r < 0 || r >= rows)
                    {
                        outOfBounds++;
                        continue;
                    }
                    int i = r * cols + c;
                    bool isWalkable = walkable[i];
                    if (isWalkable) ok++;
                    else switch (unwalkableReason[i])
                    {
                        case ReasonNoGround: noGround++; break;
                        case ReasonWater: water++; break;
                        case ReasonObstructed: obstructed++; break;
                    }

                    if ((isWalkable || unwalkableReason[i] == ReasonWater) &&
                        MapColorSampler.TryGetColorAt(CellWorldPos(c, r), out Color32 sample))
                    {
                        // NOT `(isWalkable ? walkableColor : waterColor).Add(sample)` -
                        // ColorStats is a struct, so a ternary between two struct
                        // locals yields a temporary copy; .Add on that mutates the
                        // copy and silently discards it. Confirmed live: every
                        // "color calibration" log line was missing from a whole
                        // test session despite walkable cells being present in
                        // every box scanned. Explicit if/else mutates the real
                        // local instead.
                        if (isWalkable) walkableColor.Add(sample);
                        else waterColor.Add(sample);
                    }
                }
            }
            MelonLogger.Msg($"[Minimap] TerrainGrid: local reason breakdown around {label} cell (col={centerCol},row={centerRow}), " +
                $"{LocalBreakdownRadiusCells * 2 + 1}x{LocalBreakdownRadiusCells * 2 + 1} box - " +
                $"{ok} walkable, {noGround} no-ground, {water} water, {obstructed} obstructed, {outOfBounds} out-of-bounds.");
            if (waterColor.Count > 0 || walkableColor.Count > 0)
            {
                MelonLogger.Msg($"[Minimap] TerrainGrid: color calibration around {label} cell - " +
                    $"water-labeled: {waterColor}; walkable-labeled: {walkableColor}");
            }
        }

        // Accumulates per-channel min/avg/max over a set of map-art color
        // samples - see LogLocalReasonBreakdown's calibration log.
        struct ColorStats
        {
            int count;
            int sumR, sumG, sumB;
            int minR = 255, minG = 255, minB = 255;
            int maxR, maxG, maxB;

            public ColorStats() { }

            public int Count => count;

            public void Add(Color32 c)
            {
                count++;
                sumR += c.r; sumG += c.g; sumB += c.b;
                if (c.r < minR) minR = c.r; if (c.r > maxR) maxR = c.r;
                if (c.g < minG) minG = c.g; if (c.g > maxG) maxG = c.g;
                if (c.b < minB) minB = c.b; if (c.b > maxB) maxB = c.b;
            }

            public override string ToString()
            {
                if (count == 0) return "n=0";
                return $"n={count} avgRGB=({sumR / count},{sumG / count},{sumB / count}) " +
                    $"minRGB=({minR},{minG},{minB}) maxRGB=({maxR},{maxG},{maxB})";
            }
        }

        static int lastBoundaryLogIdx = -1;

        // A component that doesn't touch the outer rim of the currently
        // baked grid is already fully determined - every one of its
        // neighbouring cells is already inside the baked area and was
        // already found non-walkable/blocked, so no cell outside the
        // current rectangle could ever become newly adjacent to it.
        // Widening the bake box only ever adds cells further out; if
        // nothing in the component even reaches the current edge, that new
        // area can't attach to it no matter how wide the box gets - used by
        // FindPath's retry loop to stop escalating once neither side could
        // possibly benefit from a wider bake (e.g. a narrow-crossing/edge-
        // aliasing disconnect fully inside already-sampled territory).
        static bool ComponentTouchesBoundary(HashSet<int> component)
        {
            foreach (int idx in component)
            {
                int col = idx % cols, row = idx / cols;
                if (col == 0 || col == cols - 1 || row == 0 || row == rows - 1)
                    return true;
            }
            return false;
        }

        // For a small connected component (its full member set is `closed`
        // - free to pass in on the start side, since A* run to exhaustion
        // already visited exactly it), finds every edge from a member cell
        // to a WALKABLE neighbour that ISN'T a member, and tallies exactly
        // which check rejected each one. Answers "is this component tiny
        // because the cells themselves are unwalkable (already covered by
        // LogLocalReasonBreakdown) or because otherwise-fine walkable cells
        // are cut off by an edge/corner check" - the latter is the signature
        // of a passage narrower than the 2m sampling grid or the obstacle
        // capsule diameter (e.g. a bridge/dock too narrow for both flanking
        // cell centres to sample clear), a long-suspected but never
        // confirmed theory for this project (see the very first "left
        // island" investigation) - user-drawn screenshot evidence
        // (2026-09-18) of a sharp working/not-working boundary at exactly
        // such a narrow crossing prompted actually checking this instead of
        // continuing to guess.
        static void LogComponentBoundary(HashSet<int> component, string label)
        {
            // component.Count is already capped at SmallComponentLogThreshold
            // by the caller, and it's a HashSet snapshot from this bake, not
            // stable across calls - re-derive a representative idx to dedupe
            // against instead of assuming an enumeration order.
            int repIdx = -1;
            foreach (int m in component) { repIdx = m; break; }
            if (repIdx == lastBoundaryLogIdx)
                return;
            lastBoundaryLogIdx = repIdx;

            int crossings = 0, orthBlocked = 0, diagBlocked = 0, cornerOrthBlocked = 0, unblocked = 0;
            foreach (int idx in component)
            {
                int col = idx % cols, row = idx / cols;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dc == 0 && dr == 0) continue;
                        int nc = col + dc, nr = row + dr;
                        if (nc < 0 || nc >= cols || nr < 0 || nr >= rows) continue;
                        int nIdx = nr * cols + nc;
                        if (!walkable[nIdx] || component.Contains(nIdx)) continue;

                        crossings++;
                        if (dc != 0 && dr != 0)
                        {
                            int orthA = row * cols + nc, orthB = nr * cols + col;
                            if (!walkable[orthA] || !walkable[orthB]) cornerOrthBlocked++;
                            else if (IsOrthogonalEdgeBlocked(col, row, dc, 0) || IsOrthogonalEdgeBlocked(col, row, 0, dr)) orthBlocked++;
                            else if (IsDiagonalEdgeBlocked(col, row, dc, dr)) diagBlocked++;
                            else unblocked++; // shouldn't happen if FindPath and this loop agree - flags a logic mismatch if it does
                        }
                        else
                        {
                            if (IsOrthogonalEdgeBlocked(col, row, dc, dr)) orthBlocked++;
                            else unblocked++;
                        }
                    }
                }
            }
            MelonLogger.Msg($"[Minimap] TerrainGrid: {label} component ({component.Count} cells) boundary - " +
                $"{crossings} edges to outside walkable cells: {orthBlocked} orthogonal-edge-blocked, " +
                $"{diagBlocked} diagonal-edge-blocked, {cornerOrthBlocked} corner-neighbour-unwalkable, " +
                $"{unblocked} UNEXPECTEDLY UNBLOCKED (logic mismatch if nonzero).");
        }

        static float SqXZDistance(int idx, Vector3 p)
        {
            Vector3 c = CellWorldPos(idx % cols, idx / cols);
            float dx = c.x - p.x, dz = c.z - p.z;
            return dx * dx + dz * dz;
        }

        // Flood-fills the destination's connected component (mirroring
        // FindPath's exact neighbour/edge/corner-cut rules, not a looser
        // 4-directional check, so this reflects what the pathfinder itself
        // can actually traverse) while tracking whichever visited cell
        // lies closest (straight-line XZ) to `referencePoint` - the
        // player's real, possibly-unroutable position. Returns the path
        // from that nearest cell to the destination (via the BFS parent
        // pointers, already in nearest-first order since the flood fill is
        // rooted at the destination) plus the component's total size AND
        // full membership (needed by FindPath's ComponentTouchesBoundary
        // check and by LogComponentBoundary's "end" diagnostic - both need
        // more than just the count).
        static List<Vector3> FindNearestReachablePathToDestination(int endIdx, Vector3 referencePoint, out int componentSize, out HashSet<int> component)
        {
            var visited = new HashSet<int> { endIdx };
            var cameFrom = new Dictionary<int, int>();
            var queue = new Queue<int>();
            queue.Enqueue(endIdx);

            int nearestIdx = endIdx;
            float bestSqDist = SqXZDistance(endIdx, referencePoint);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int curCol = current % cols, curRow = current / cols;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dc == 0 && dr == 0)
                            continue;
                        int nc = curCol + dc, nr = curRow + dr;
                        if (nc < 0 || nc >= cols || nr < 0 || nr >= rows)
                            continue;
                        int nIdx = nr * cols + nc;
                        if (!walkable[nIdx] || visited.Contains(nIdx))
                            continue;
                        if (dc != 0 && dr != 0)
                        {
                            int orthA = curRow * cols + nc;
                            int orthB = nr * cols + curCol;
                            if (!walkable[orthA] || !walkable[orthB])
                                continue;
                            if (IsOrthogonalEdgeBlocked(curCol, curRow, dc, 0) || IsOrthogonalEdgeBlocked(curCol, curRow, 0, dr))
                                continue;
                            if (IsDiagonalEdgeBlocked(curCol, curRow, dc, dr))
                                continue;
                        }
                        else if (IsOrthogonalEdgeBlocked(curCol, curRow, dc, dr))
                        {
                            continue;
                        }
                        visited.Add(nIdx);
                        cameFrom[nIdx] = current;
                        queue.Enqueue(nIdx);
                        float d = SqXZDistance(nIdx, referencePoint);
                        if (d < bestSqDist)
                        {
                            bestSqDist = d;
                            nearestIdx = nIdx;
                        }
                    }
                }
            }

            componentSize = visited.Count;
            component = visited;

            // Parent pointers were built expanding OUT from endIdx (the
            // root), so walking them back from nearestIdx already lands in
            // nearest-first order - nearestIdx -> ... -> endIdx - with no
            // reversal needed (unlike ReconstructPath below, which walks
            // from the far end of a search rooted at the start).
            var cells = new List<int> { nearestIdx };
            int cur = nearestIdx;
            while (cameFrom.TryGetValue(cur, out int prev))
            {
                cur = prev;
                cells.Add(cur);
            }

            var result = new List<Vector3>(cells.Count);
            foreach (int idx in cells)
                result.Add(CellWorldPos(idx % cols, idx / cols));
            return result;
        }

        static List<Vector3> ReconstructPath(Dictionary<int, int> cameFrom, int endIdx)
        {
            var cells = new List<int> { endIdx };
            int cur = endIdx;
            while (cameFrom.TryGetValue(cur, out int prev))
            {
                cur = prev;
                cells.Add(cur);
            }
            cells.Reverse();

            var result = new List<Vector3>(cells.Count);
            foreach (int idx in cells)
                result.Add(CellWorldPos(idx % cols, idx / cols));
            return result;
        }
    }
}
