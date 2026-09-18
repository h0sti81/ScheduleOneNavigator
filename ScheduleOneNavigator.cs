using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.UI;

[assembly: MelonInfo(typeof(ScheduleOneNavigator.ScheduleOneNavigatorMod), "ScheduleOne Navigator", "0.2.0", "h0sti")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace ScheduleOneNavigator
{
    public class ScheduleOneNavigatorMod : MelonMod
    {
        // --- Tuning constants: adjust to taste ---
        const float MapPixelSize = 220f;      // on-screen size of the minimap panel (square)
        const int RenderTextureSize = 512;    // resolution of the top-down camera capture
        const float OrthoSize = 40f;          // world units shown from map center to edge
        const float CameraHeight = 80f;       // how far above the player the capture camera hovers
        const float MarkerIconSize = 6f;
        const float PlayerArrowSize = 18f;
        const float DestinationIconSize = 14f;
        const float MarkerRefreshInterval = 1f; // seconds between rescanning customers/other players
        const float RouteDotSpacing = 5f;   // world units between route dots
        const float RouteDotSize = 6f;
        const int MaxRouteDots = 100;
        const float RouteFadeStartFraction = 0.75f; // start fading dots out at 75% of the way to the map edge

        static readonly Color BackgroundColor = new Color(0.05f, 0.07f, 0.06f, 1f);
        static readonly Color BorderColor = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color PlayerArrowColor = new Color(0.96f, 0.62f, 0.13f, 1f);
        static readonly Color PlayerArrowOutlineColor = new Color(0.72f, 0.42f, 0.06f, 1f);
        static readonly Color CustomerColor = new Color(0.9f, 0.25f, 0.25f, 0.9f);
        static readonly Color DestinationColor = new Color(0.25f, 0.85f, 1f, 1f);
        static readonly Color OtherPlayerColor = new Color(0.25f, 0.85f, 0.35f, 0.95f);

        Camera minimapCamera;
        RenderTexture minimapRT;
        RectTransform minimapRoot;
        RectTransform playerArrow;

        Sprite customerAuraSprite;
        Sprite otherPlayerSprite;
        RectTransform destinationMarker;

        readonly Dictionary<Customer, RectTransform> customerMarkers = new Dictionary<Customer, RectTransform>();
        readonly Dictionary<Player, RectTransform> otherPlayerMarkers = new Dictionary<Player, RectTransform>();

        readonly RoutePlanner routePlanner = new RoutePlanner();
        FullMapView fullMapView;
        readonly List<RectTransform> routeDotPool = new List<RectTransform>();
        readonly List<Image> routeDotImagePool = new List<Image>();
        Sprite routeDotSprite;

        float markerRefreshTimer;
        bool initialized;

        public override void OnUpdate()
        {
            Player player = Player.Local;
            if (player == null)
                return;

            if (fullMapView == null)
                fullMapView = new FullMapView(routePlanner);
            fullMapView.Tick();

            if (initialized && minimapRoot == null)
            {
                // Everything we parented under the HUD canvas (minimapRoot and all
                // its children: arrow, markers, route dots) gets destroyed if that
                // canvas is ever torn down and recreated (observed in testing:
                // NullReferenceException every frame in UpdatePlayerArrow ->
                // Transform.set_localEulerAngles, right after entering/leaving a
                // building) - but our fields still point at the now-destroyed
                // objects, so every update below would throw forever and the
                // minimap would just vanish for the rest of the session. Force a
                // clean re-init.
                initialized = false;
                if (minimapCamera != null)
                    GameObject.Destroy(minimapCamera.gameObject);
                if (minimapRT != null)
                    minimapRT.Release();
                customerMarkers.Clear();
                otherPlayerMarkers.Clear();
                routeDotPool.Clear();
                routeDotImagePool.Clear();
            }

            if (!initialized)
            {
                TryInitialize();
                if (!initialized)
                    return;
            }

            UpdateCamera(player);
            UpdatePlayerArrow(player);

            markerRefreshTimer -= Time.deltaTime;
            if (markerRefreshTimer <= 0f)
            {
                markerRefreshTimer = MarkerRefreshInterval;
                RefreshCustomerMarkers();
                RefreshOtherPlayerMarkers();
            }

            UpdateMarkerPositions(customerMarkers, player, clampToEdge: false);
            UpdateMarkerPositions(otherPlayerMarkers, player, clampToEdge: false);

            routePlanner.Tick();
            UpdateRouteDots(player);
            UpdateDestinationMarker(player);
        }

        // The route line itself only draws within the minimap's small OrthoSize
        // radius (see UpdateRouteDots), so a destination further away than that -
        // the common case, since routes are typically hundreds of meters - never
        // shows up on screen at all. Without this, every click looks identical:
        // you only ever see the same short lead-out segment near the player,
        // regardless of where the actual (correctly-computed, per-click-varying)
        // destination is. Clamping a single marker to the map edge, the same way
        // out-of-range property markers already work, makes the current
        // destination's direction visible immediately.
        void UpdateDestinationMarker(Player player)
        {
            if (!routePlanner.HasDestination)
            {
                destinationMarker.gameObject.SetActive(false);
                return;
            }

            Vector3 playerPos = GetTrackedPosition(player);
            Vector3 dest = routePlanner.Destination;
            float half = MapPixelSize / 2f;

            float nx = (dest.x - playerPos.x) / OrthoSize;
            float nz = (dest.z - playerPos.z) / OrthoSize;

            if (Mathf.Abs(nx) > 1f || Mathf.Abs(nz) > 1f)
            {
                float len = Mathf.Sqrt(nx * nx + nz * nz);
                nx /= len;
                nz /= len;
            }

            destinationMarker.gameObject.SetActive(true);
            destinationMarker.anchoredPosition = new Vector2(nx * half, nz * half);
        }

        void TryInitialize()
        {
            HUD hud = HUD.Instance;
            if (hud == null || hud.canvas == null)
                return; // HUD not ready yet (still loading into the game)

            Canvas parentCanvas = hud.canvas;

            // --- Top-down capture camera ---
            GameObject camGO = new GameObject("MinimapCamera");
            GameObject.DontDestroyOnLoad(camGO);
            minimapCamera = camGO.AddComponent<Camera>();
            minimapCamera.orthographic = true;
            minimapCamera.orthographicSize = OrthoSize;
            minimapCamera.nearClipPlane = 1f;
            minimapCamera.farClipPlane = CameraHeight + 40f;
            minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = BackgroundColor;
            minimapCamera.cullingMask = ~0;
            minimapCamera.depth = -10f;
            minimapCamera.aspect = 1f;

            minimapRT = new RenderTexture(RenderTextureSize, RenderTextureSize, 16);
            minimapRT.Create();
            minimapCamera.targetTexture = minimapRT;

            // --- UI: panel anchored top-right of the existing HUD canvas ---
            GameObject rootGO = new GameObject("MinimapRoot");
            rootGO.transform.SetParent(parentCanvas.transform, false);
            minimapRoot = rootGO.AddComponent<RectTransform>();
            minimapRoot.anchorMin = new Vector2(1f, 1f);
            minimapRoot.anchorMax = new Vector2(1f, 1f);
            minimapRoot.pivot = new Vector2(1f, 1f);
            minimapRoot.sizeDelta = new Vector2(MapPixelSize, MapPixelSize);
            minimapRoot.anchoredPosition = new Vector2(-20f, -20f);

            GameObject borderGO = new GameObject("Border");
            borderGO.transform.SetParent(minimapRoot, false);
            RectTransform borderRT = borderGO.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = new Vector2(-4f, -4f);
            borderRT.offsetMax = new Vector2(4f, 4f);
            borderGO.AddComponent<Image>().color = BorderColor;

            GameObject mapGO = new GameObject("MapImage");
            mapGO.transform.SetParent(minimapRoot, false);
            RectTransform mapRT = mapGO.AddComponent<RectTransform>();
            mapRT.anchorMin = Vector2.zero;
            mapRT.anchorMax = Vector2.one;
            mapRT.offsetMin = Vector2.zero;
            mapRT.offsetMax = Vector2.zero;
            mapGO.AddComponent<RawImage>().texture = minimapRT;

            // Subtle aura marking recruitable NPCs (see RefreshCustomerMarkers)
            // - low peak alpha, soft radial falloff, no solid dot.
            customerAuraSprite = CreateGlowSprite(48, new Color(CustomerColor.r, CustomerColor.g, CustomerColor.b, 0.3f));
            otherPlayerSprite = CreateCircleSprite(24, OtherPlayerColor);
            routeDotSprite = CreateCircleSprite(16, Color.white);

            destinationMarker = CreateMarkerIcon("DestinationMarker", CreateCircleSprite(28, DestinationColor), DestinationIconSize);
            destinationMarker.gameObject.SetActive(false);

            GameObject arrowGO = new GameObject("PlayerArrow");
            arrowGO.transform.SetParent(minimapRoot, false);
            playerArrow = arrowGO.AddComponent<RectTransform>();
            playerArrow.sizeDelta = new Vector2(PlayerArrowSize, PlayerArrowSize);
            playerArrow.anchorMin = playerArrow.anchorMax = new Vector2(0.5f, 0.5f);
            playerArrow.anchoredPosition = Vector2.zero;
            Image arrowImg = arrowGO.AddComponent<Image>();
            arrowImg.sprite = CreateArrowSprite(32, PlayerArrowColor, PlayerArrowOutlineColor);
            arrowImg.type = Image.Type.Simple;
            arrowImg.color = Color.white;

            initialized = true;
            MelonLogger.Msg("[Minimap] Initialized top-right minimap.");
        }

        // The player's own transform doesn't reliably track world position
        // while seated in a vehicle (confirmed live 2026-09-19: the minimap
        // camera stopped following entirely while driving) - the vehicle
        // seat, a child of the vehicle itself, does. Used everywhere "where
        // is this player right now" is needed, for both the local player and
        // (via otherPlayerMarkers) other players in multiplayer.
        public static Vector3 GetTrackedPosition(Player player)
        {
            if (player.IsInVehicle && player.CurrentVehicleSeat != null)
                return player.CurrentVehicleSeat.transform.position;
            return player.transform.position;
        }

        public static float GetTrackedYaw(Player player)
        {
            if (player.IsInVehicle && player.CurrentVehicleSeat != null)
                return player.CurrentVehicleSeat.transform.eulerAngles.y;
            return player.transform.eulerAngles.y;
        }

        void UpdateCamera(Player player)
        {
            Vector3 p = GetTrackedPosition(player);
            minimapCamera.transform.position = new Vector3(p.x, p.y + CameraHeight, p.z);
            minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // fixed north-up
        }

        void UpdatePlayerArrow(Player player)
        {
            float yaw = GetTrackedYaw(player);
            // North-up map: rotate the arrow opposite the compass heading so it
            // points toward where the player is actually facing on screen.
            // If the arrow appears mirrored in-game, flip the sign here.
            playerArrow.localEulerAngles = new Vector3(0f, 0f, -yaw);
        }

        // Shows who's still recruitable, not who's already a customer -
        // Customer.LockedCustomers (confirmed via decompile) is every
        // Customer NPC not yet unlocked, i.e. exactly "could be recruited
        // right now". Replaced the old dot-per-unlocked-customer display
        // 2026-09-19 per user request - once someone's actually recruited,
        // the minimap doesn't need to keep pointing at them, the Deals tab
        // covers that.
        void RefreshCustomerMarkers()
        {
            var lockedList = Customer.LockedCustomers;
            HashSet<Customer> recruitable = new HashSet<Customer>();
            if (lockedList != null)
                foreach (Customer c in lockedList)
                    if (c != null)
                        recruitable.Add(c);

            List<Customer> stale = null;
            foreach (var kvp in customerMarkers)
                if (kvp.Key == null || !recruitable.Contains(kvp.Key))
                    (stale ??= new List<Customer>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(customerMarkers[key].gameObject);
                    customerMarkers.Remove(key);
                }

            foreach (Customer customer in recruitable)
                if (!customerMarkers.ContainsKey(customer))
                    customerMarkers[customer] = CreateCustomerMarkerIcon();
        }

        // Other connected players, shown as a green dot - added 2026-09-19
        // per user request. Player.PlayerList (confirmed via decompile) is
        // the full roster (local + remote); IsLocalPlayer filters our own
        // entry out the same way RefreshCustomerMarkers filters to only
        // unlocked customers.
        void RefreshOtherPlayerMarkers()
        {
            HashSet<Player> others = new HashSet<Player>();
            var allPlayers = Player.PlayerList;
            if (allPlayers != null)
                foreach (Player p in allPlayers)
                    if (p != null && !p.IsLocalPlayer)
                        others.Add(p);

            List<Player> stale = null;
            foreach (var kvp in otherPlayerMarkers)
                if (kvp.Key == null || !others.Contains(kvp.Key))
                    (stale ??= new List<Player>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(otherPlayerMarkers[key].gameObject);
                    otherPlayerMarkers.Remove(key);
                }

            foreach (Player p in others)
                if (!otherPlayerMarkers.ContainsKey(p))
                    otherPlayerMarkers[p] = CreateMarkerIcon("OtherPlayerMarker", otherPlayerSprite, MarkerIconSize);
        }

        RectTransform CreateCustomerMarkerIcon()
        {
            GameObject containerGO = new GameObject("CustomerMarker");
            containerGO.transform.SetParent(minimapRoot, false);
            RectTransform containerRT = containerGO.AddComponent<RectTransform>();
            containerRT.sizeDelta = new Vector2(MarkerIconSize, MarkerIconSize);
            containerRT.anchorMin = containerRT.anchorMax = new Vector2(0.5f, 0.5f);

            GameObject auraGO = new GameObject("Aura");
            auraGO.transform.SetParent(containerRT, false);
            RectTransform auraRT = auraGO.AddComponent<RectTransform>();
            auraRT.sizeDelta = new Vector2(MarkerIconSize * 2.5f, MarkerIconSize * 2.5f);
            auraRT.anchorMin = auraRT.anchorMax = new Vector2(0.5f, 0.5f);
            Image auraImg = auraGO.AddComponent<Image>();
            auraImg.sprite = customerAuraSprite;
            auraImg.color = Color.white;

            return containerRT;
        }

        RectTransform CreateMarkerIcon(string name, Sprite sprite, float size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(minimapRoot, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            return rt;
        }

        void UpdateMarkerPositions<TKey>(Dictionary<TKey, RectTransform> markers, Player player, bool clampToEdge)
            where TKey : Component
        {
            Vector3 playerPos = GetTrackedPosition(player);
            float half = MapPixelSize / 2f;

            foreach (var kvp in markers)
            {
                TKey source = kvp.Key;
                RectTransform rt = kvp.Value;
                if (source == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }

                // A source that's itself a Player (otherPlayerMarkers) needs
                // the same vehicle-seat tracking as the local player above,
                // in case that other player is currently driving too.
                Player sourceAsPlayer = source.TryCast<Player>();
                Vector3 worldPos = sourceAsPlayer != null ? GetTrackedPosition(sourceAsPlayer) : source.transform.position;
                float nx = (worldPos.x - playerPos.x) / OrthoSize;
                float nz = (worldPos.z - playerPos.z) / OrthoSize;

                bool outside = Mathf.Abs(nx) > 1f || Mathf.Abs(nz) > 1f;
                if (outside)
                {
                    if (!clampToEdge)
                    {
                        rt.gameObject.SetActive(false);
                        continue;
                    }
                    float len = Mathf.Sqrt(nx * nx + nz * nz);
                    nx /= len;
                    nz /= len;
                }

                rt.gameObject.SetActive(true);
                rt.anchoredPosition = new Vector2(nx * half, nz * half);
            }
        }

        void UpdateRouteDots(Player player)
        {
            int used = 0;
            var waypoints = routePlanner.Waypoints;

            if (waypoints.Count >= 2)
            {
                Vector3 playerPos = GetTrackedPosition(player);
                float half = MapPixelSize / 2f;
                float distanceUntilNextDot = 0f;

                for (int i = 0; i < waypoints.Count - 1 && used < MaxRouteDots; i++)
                {
                    Vector3 segStart = waypoints[i];
                    Vector3 segEnd = waypoints[i + 1];
                    Vector3 segVec = segEnd - segStart;
                    float segLen = segVec.magnitude;
                    if (segLen < 0.001f)
                        continue;

                    Vector3 segDir = segVec / segLen;
                    float posAlongSeg = distanceUntilNextDot;
                    while (posAlongSeg < segLen && used < MaxRouteDots)
                    {
                        Vector3 dotWorld = segStart + segDir * posAlongSeg;
                        float nx = (dotWorld.x - playerPos.x) / OrthoSize;
                        float nz = (dotWorld.z - playerPos.z) / OrthoSize;
                        float edgeDist = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(nz)); // 0 = center, 1 = square map edge

                        if (edgeDist <= 1f)
                        {
                            RectTransform dot = GetOrCreateRouteDot(used);
                            dot.gameObject.SetActive(true);
                            dot.anchoredPosition = new Vector2(nx * half, nz * half);

                            float alpha = edgeDist <= RouteFadeStartFraction
                                ? 1f
                                : Mathf.Clamp01(1f - (edgeDist - RouteFadeStartFraction) / (1f - RouteFadeStartFraction));
                            Color c = routeDotImagePool[used].color;
                            c.a = alpha;
                            routeDotImagePool[used].color = c;

                            used++;
                        }

                        posAlongSeg += RouteDotSpacing;
                    }

                    distanceUntilNextDot = posAlongSeg - segLen;
                }
            }

            for (int i = used; i < routeDotPool.Count; i++)
                routeDotPool[i].gameObject.SetActive(false);
        }

        RectTransform GetOrCreateRouteDot(int index)
        {
            if (index < routeDotPool.Count)
                return routeDotPool[index];

            RectTransform dot = CreateMarkerIcon("RouteDot", routeDotSprite, RouteDotSize);
            routeDotPool.Add(dot);
            routeDotImagePool.Add(dot.GetComponent<Image>());
            return dot;
        }

        internal static Sprite CreateCircleSprite(int size, Color color)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            float radius = size / 2f;
            Vector2 center = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    pixels[y * size + x] = d <= radius ? (Color32)color : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // Soft radial-gradient circle (full alpha at center, fading to 0 at
        // the edge) - used for the subtle "potential customer" aura, as
        // opposed to CreateCircleSprite's hard-edged dot. `color`'s own alpha
        // is the peak alpha at the very center.
        internal static Sprite CreateGlowSprite(int size, Color color)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            float radius = size / 2f;
            Vector2 center = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float falloff = Mathf.Clamp01(1f - d / radius);
                    // Smoothstep-ish falloff (falloff^2) reads softer/more
                    // "glow-like" than a linear ramp.
                    Color c = color;
                    c.a = color.a * (falloff * falloff);
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // Navigation-style "kite" arrow: pointed tip, two wing corners, and a concave
        // notch at the back (like the classic GPS heading marker), drawn as an outline
        // pass followed by a slightly smaller fill pass to get a clean border.
        static readonly Vector2[] ArrowKiteShape =
        {
            new Vector2(0f, 1f),       // tip
            new Vector2(0.62f, -0.55f),  // right wing
            new Vector2(0f, -0.05f),     // concave back notch
            new Vector2(-0.62f, -0.55f), // left wing
        };

        static Sprite CreateArrowSprite(int size, Color fillColor, Color outlineColor)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            FillKitePolygon(pixels, size, 1f, outlineColor);
            FillKitePolygon(pixels, size, 0.8f, fillColor);

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        static void FillKitePolygon(Color32[] pixels, int size, float scale, Color color)
        {
            float cx = (size - 1) / 2f;
            float cy = (size - 1) / 2f;
            float radius = (size / 2f) * scale;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // map pixel to the same [-1, 1] space the kite shape is defined in
                    Vector2 p = new Vector2((x - cx) / radius, (y - cy) / radius);
                    if (IsInsideKite(p))
                        pixels[y * size + x] = color;
                }
            }
        }

        static bool IsInsideKite(Vector2 p)
        {
            bool inside = false;
            int n = ArrowKiteShape.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = ArrowKiteShape[i];
                Vector2 b = ArrowKiteShape[j];
                if (((a.y > p.y) != (b.y > p.y)) &&
                    (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x))
                    inside = !inside;
            }
            return inside;
        }
    }
}
