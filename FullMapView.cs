using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MelonLoader;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Map;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.NPCs;

namespace ScheduleOneNavigator
{
    // A full-screen map view we build and own entirely ourselves (toggled with
    // '#'), instead of hooking into the phone app's own zoomable/scrollable map
    // UI. That earlier approach kept breaking in ways tied to the phone UI's
    // own internal state: a same-frame layout-rebuild race right after its
    // zoom changed, an unexplained mismatch between GetMapPosition()'s
    // coordinate space and the PoIContainer RectTransform's local space, etc -
    // all sources of bugs that exist only because we were reverse-engineering
    // someone else's UI we don't control the update timing of.
    //
    // Here, the pan/zoom transform (mapContainer.anchoredPosition/localScale)
    // and the click read (Input.GetMouseButtonDown) both happen in the same
    // Tick(), in a fixed order we control - so there is no cross-system timing
    // race, and no separate coordinate space to reconcile: mapContainer's own
    // local space is *defined* (via sizeDelta = MapDimensions and
    // anchoredPosition = -playerMapPos*zoom) to already be GetMapPosition()'s
    // coordinate space, by construction, independent of pivot - see the pivot
    // comment down in EnsureCreated for why it's centered, not top-left.
    //
    // Visual design intentionally echoes the in-game phone (dark bezel,
    // header bar) but scaled up like a tablet, per user request - it should
    // feel like a bigger version of the phone map, not a bare debug overlay.
    public class FullMapView
    {
        const float DefaultZoom = 0.5f;
        const float MinZoom = 0.08f;
        const float MaxZoom = 3f;
        const float DeviceFraction = 0.8f; // fraction of the smaller screen dimension used by the tablet's screen area
        const float HeaderHeight = 56f;
        const float FooterHeight = 36f;
        const float BezelPadding = 14f;
        const float PlayerDotSize = 14f;
        const float RouteDotSize = 8f;
        const float DestinationDotSize = 16f;
        const int MaxRouteDots = 150;
        const float CustomerDotSize = 12.8f;
        const float CustomerClickPadding = 10f; // extra hit-test margin around each small marker, easier to click
        const float DealPanelWidth = 300f;
        const float DealRowHeight = 66f;
        const float DealListTitleHeight = 30f;
        const float DealButtonHeight = 40f;
        const float DealRefreshInterval = 1f;
        const float TabRowHeight = 32f;
        const float BusinessRowHeight = 40f;
        const float BusinessDotSize = 12.8f;
        const float BusinessClickPadding = 10f;
        const float BusinessRefreshInterval = 1f;
        const float ShopRowHeight = 40f;
        const float ShopDotSize = 12.8f;
        const float ShopClickPadding = 10f;
        const float ShopRefreshInterval = 1f;
        const float DealerRowHeight = 40f;
        const float SectionHeaderRowHeight = 24f;
        const float DealerDotSize = 12.8f;
        const float DealerClickPadding = 10f;
        const float DealerRefreshInterval = 1f;
        const float PropertyRowHeight = 40f;
        const float PropertyDotSize = 12.8f;
        const float PropertyClickPadding = 10f;
        const float PropertyRefreshInterval = 1f;
        const float OtherPlayerDotSize = 12.8f;
        const float OtherPlayerRefreshInterval = 1f;

        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color BezelColor = new Color(0.07f, 0.08f, 0.08f, 1f);
        static readonly Color HeaderColor = new Color(0.11f, 0.12f, 0.13f, 1f);
        static readonly Color ScreenBgColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        static readonly Color TitleColor = new Color(0.92f, 0.93f, 0.94f, 1f);
        static readonly Color HintColor = new Color(0.6f, 0.62f, 0.64f, 1f);
        static readonly Color PlayerDotColor = new Color(0.96f, 0.62f, 0.13f, 1f);
        static readonly Color RouteDotColor = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color DestinationDotColor = new Color(0.25f, 0.85f, 1f, 1f);
        static readonly Color CustomerDotColor = new Color(0.9f, 0.25f, 0.25f, 0.95f);
        static readonly Color DealPanelColor = new Color(0.09f, 0.1f, 0.11f, 1f);
        static readonly Color DealRowFeasibleColor = new Color(0.16f, 0.28f, 0.17f, 1f);
        static readonly Color DealRowInfeasibleColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        static readonly Color DealRowTextColor = new Color(0.93f, 0.94f, 0.95f, 1f);
        static readonly Color DealRowSubTextColor = new Color(0.65f, 0.67f, 0.69f, 1f);
        static readonly Color DealFeasibleTagColor = new Color(0.4f, 0.85f, 0.45f, 1f);
        static readonly Color DealInfeasibleTagColor = new Color(0.8f, 0.35f, 0.3f, 1f);
        static readonly Color RouteButtonColor = new Color(0.2f, 0.45f, 0.75f, 1f);
        static readonly Color RouteButtonDisabledColor = new Color(0.2f, 0.21f, 0.23f, 1f);
        static readonly Color BusinessDotColor = new Color(0.25f, 0.75f, 0.7f, 0.95f); // teal
        static readonly Color BusinessRowColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        static readonly Color ShopDotColor = new Color(0.65f, 0.4f, 0.85f, 0.95f); // purple, distinct from businesses/customers/player
        static readonly Color ShopRowColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        static readonly Color DealerDotColor = new Color(0.95f, 0.85f, 0.2f, 0.95f); // gold, distinct from all other markers
        static readonly Color DealerDotColorLocked = new Color(0.5f, 0.47f, 0.35f, 0.7f); // dimmed gold - not yet unlocked
        static readonly Color DealerRowAvailableColor = new Color(0.16f, 0.28f, 0.17f, 1f); // mirrors DealRowFeasibleColor
        static readonly Color DealerRowUnavailableColor = new Color(0.16f, 0.16f, 0.17f, 1f); // mirrors DealRowInfeasibleColor
        static readonly Color PropertyDotColor = new Color(1f, 0.85f, 0.15f, 1f); // same yellow the small minimap used to use
        static readonly Color PropertyRowOwnedColor = new Color(0.16f, 0.28f, 0.17f, 1f);
        static readonly Color PropertyRowUnownedColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        static readonly Color OtherPlayerDotColor = new Color(0.25f, 0.85f, 0.35f, 0.95f); // same green as the small minimap
        static readonly Color SectionHeaderRowColor = new Color(0.11f, 0.12f, 0.13f, 1f);
        static readonly Color SectionHeaderTextColor = new Color(0.65f, 0.67f, 0.69f, 1f);
        static readonly Color TabActiveColor = new Color(0.2f, 0.45f, 0.75f, 1f);
        static readonly Color TabInactiveColor = new Color(0.14f, 0.15f, 0.16f, 1f);

        readonly RoutePlanner routePlanner;

        GameObject rootGO;
        RectTransform viewport;
        RectTransform mapContainer;
        Image mapImage;
        Text hintText;
        RectTransform playerDot;
        RectTransform destinationDot;
        readonly List<RectTransform> routeDotPool = new List<RectTransform>();
        readonly Dictionary<Customer, RectTransform> customerMarkers = new Dictionary<Customer, RectTransform>();
        Sprite customerSprite;

        RectTransform dealListContainer;
        RectTransform dealListViewportRT;
        RectTransform dealTitleRT;
        Text dealTitleText;
        GameObject routeButtonGO;
        RectTransform routeButtonRect;
        Image routeButtonImage;
        Text routeButtonText;
        readonly List<DealInfo> currentDeals = new List<DealInfo>();
        readonly List<(RectTransform rect, DealInfo deal)> dealRows = new List<(RectTransform, DealInfo)>();
        readonly List<RectTransform> dealHeaderRows = new List<RectTransform>();
        float dealRefreshTimer;

        // Businesses tab - the player's/all drug-production properties
        // (Warehouse, Sweatshop, Motel, ...). Sourced from Business (a
        // physically-placed NetworkBehaviour, same as Customer/Property in
        // ScheduleOneNavigator.cs) - this used to sit on the "Shops" tab by mistake
        // (see session notes 2026-09-18); moved to its own tab 2026-09-19,
        // since "Shops" (Gas Mart, Pawn Shop, ...) is a distinct in-game
        // category.
        readonly Dictionary<Business, RectTransform> businessMarkers = new Dictionary<Business, RectTransform>();
        Sprite businessSprite;
        readonly List<Business> currentBusinesses = new List<Business>();
        readonly List<(RectTransform rect, Business business)> businessRows = new List<(RectTransform, Business)>();
        float businessRefreshTimer;

        // Shops tab - real retail locations (Gas Mart, hardware stores, Auto
        // Shop, Pawn Shop, Ray's Realty, ...). Unlike Deals/Businesses, the
        // game has no single class/list for these (confirmed via decompile,
        // 2026-09-19) - RefreshShops() pulls from several different sources.
        // Markers/rows are keyed by the underlying real Transform (whichever
        // physically-placed component turned out to hold that shop's
        // position), not by a shop-specific type, since there isn't one
        // common type to key by.
        readonly Dictionary<Transform, RectTransform> shopMarkers = new Dictionary<Transform, RectTransform>();
        Sprite shopSprite;
        readonly List<(Transform position, string name)> currentShops = new List<(Transform, string)>();
        readonly List<(RectTransform rect, Transform position, string name)> shopRows = new List<(RectTransform, Transform, string)>();
        float shopRefreshTimer;
        static readonly HashSet<string> loggedMissingShopPositions = new HashSet<string>();

        // Manual per-shop position corrections, keyed by the shop's display
        // name - for cases where the best available position source (an
        // NPC's live position, a delivery bay, ...) is technically real but
        // lands somewhere unhelpful (e.g. Auto Shop routed to Jeremy's own
        // standing spot behind the building instead of the customer
        // entrance, reported live 2026-09-19). Coordinates are read from our
        // own click-to-route log line (HandleClick logs the exact world
        // position of any map click) rather than guessed. Add further
        // entries the same way if another shop turns out to need it.
        static readonly Dictionary<string, Vector3> ShopPositionOverrides = new Dictionary<string, Vector3>
        {
            // Logged from a click on the correct front entrance, 2026-09-19 -
            // see FullMapView click log: world (5.38, 0.10, -38.85), 1.4m
            // from the player at the time.
            ["Auto Shop"] = new Vector3(5.38f, 0.10f, -38.85f),
            // Logged from a click on the player's own standing position (the
            // correct spot), 2026-09-19 - see FullMapView click log: world
            // (-130.13, -3.90, 66.62), 1.4m from the player at the time.
            ["Top Tattoos"] = new Vector3(-130.13f, -3.90f, 66.62f),
            // Logged from a click on the correct spot, 2026-09-19 - see
            // FullMapView click log: world (-112.87, -3.90, 66.30), 1.3m from
            // the player at the time.
            ["Gas-Mart (West)"] = new Vector3(-112.87f, -3.90f, 66.30f),
            // Logged from a click on the correct spot, 2026-09-19 - see
            // FullMapView click log: world (-61.42, 0.10, 54.16), 1.0m from
            // the player at the time.
            ["Pawn Shop"] = new Vector3(-61.42f, 0.10f, 54.16f),
            // Logged from a click on the correct spot, 2026-09-19 - see
            // FullMapView click log: world (28.07, 0.10, 82.60), 2.0m from
            // the player at the time. (An earlier click at (24.23, 5.08,
            // 86.76) was rejected live - TerrainGrid found it isolated in a
            // 1-cell region, disconnected from the main walkable area.)
            ["Casino"] = new Vector3(28.07f, 0.10f, 82.60f),
            // Logged from a click on the correct spot, 2026-09-19 - see
            // FullMapView click log: world (21.36, 5.45, -9.12), 4.5m from
            // the player at the time; path resolved successfully.
            ["Gas-Mart (Central)"] = new Vector3(21.36f, 5.45f, -9.12f),
            // Logged from a click on the correct spot, 2026-09-19 - see
            // FullMapView click log: world (82.72, 0.00, -8.16), 1.7m from
            // the player at the time.
            ["Ray's Realty"] = new Vector3(82.72f, 0.00f, -8.16f),
            // Logged from a later, more precise click, 2026-09-19 - see
            // FullMapView click log: world (-22.43, 0.10, 32.42) - superseded
            // the earlier (-22.11, 10.58, 33.06) attempt, which was 9.5m from
            // the player with a 16.4m pathfinding snap offset; this one has
            // only a 0.7m snap offset.
            ["Barbershop"] = new Vector3(-22.43f, 0.10f, 32.42f),
        };
        readonly Dictionary<string, Transform> shopOverrideAnchors = new Dictionary<string, Transform>();

        // Returns the override anchor Transform for `shopName` if one is
        // configured in ShopPositionOverrides (creating its backing
        // GameObject on first use), otherwise `fallback` unchanged.
        Transform ApplyShopPositionOverride(string shopName, Transform fallback)
        {
            if (!ShopPositionOverrides.TryGetValue(shopName, out Vector3 overridePos))
                return fallback;

            if (!shopOverrideAnchors.TryGetValue(shopName, out Transform anchor) || anchor == null)
            {
                GameObject go = new GameObject($"ShopPositionOverride_{shopName}");
                anchor = go.transform;
                shopOverrideAnchors[shopName] = anchor;
            }
            anchor.position = overridePos;
            return anchor;
        }

        // Dealers tab - the NPCs excluded from the Shops tab (see
        // IsDealerOrSupplierNpc): 6 "Dealer" NPCs with their own
        // ShopInterface field (Stan/Dan/Fiona/Herbert/Oscar/Steve, matches
        // the external reference map's "NPCS > DEALER: 6") plus 4 "Supplier"
        // NPCs (Albert/Phil/Salvador/Shirley). Both groups are plain
        // NPC : NetworkBehaviour, physically placed like Customer, so
        // .transform.position works directly - no position-source problem
        // like on the Shops tab. Added 2026-09-19 per user request, instead
        // of silently dropping them.
        readonly Dictionary<NPC, RectTransform> dealerMarkers = new Dictionary<NPC, RectTransform>();
        Sprite dealerSprite;
        Sprite dealerSpriteLocked;
        readonly List<NPC> currentDealerNpcs = new List<NPC>();
        readonly List<(RectTransform rect, NPC npc)> dealerRows = new List<(RectTransform, NPC)>();
        readonly List<RectTransform> dealerHeaderRows = new List<RectTransform>();
        float dealerRefreshTimer;

        // Properties tab - every Property (the base class Business also
        // derives from, so this covers both plain real estate like the
        // Motel/Barn/Bungalow and the drug-production Businesses in one
        // list) via Property.Properties, the full roster regardless of
        // ownership. Added 2026-09-19 replacing the small minimap's yellow
        // owned-property markers - those only ever showed what's already
        // owned; this shows everything, with unowned entries grayed out and
        // not clickable/markered (see RefreshPropertyList/RefreshPropertyMarkers).
        readonly Dictionary<Property, RectTransform> propertyMarkers = new Dictionary<Property, RectTransform>();
        Sprite propertySprite;
        readonly List<Property> currentProperties = new List<Property>();
        readonly List<(RectTransform rect, Property property)> propertyRows = new List<(RectTransform, Property)>();
        readonly List<RectTransform> propertyHeaderRows = new List<RectTransform>();
        float propertyRefreshTimer;

        // Other multiplayer players (green dot) - ambient, shown on every
        // tab regardless of viewMode, mirroring the small minimap's own
        // otherPlayerMarkers (see ScheduleOneNavigator.cs). Not clickable -
        // there's nothing to route to.
        readonly Dictionary<Player, RectTransform> otherPlayerMarkers = new Dictionary<Player, RectTransform>();
        Sprite otherPlayerSprite;
        float otherPlayerRefreshTimer;

        // Tab strip - see EnsureCreated for how these are built.
        enum MapViewMode { Deals, Shops, Businesses, Dealers, Properties }
        MapViewMode viewMode = MapViewMode.Deals;
        readonly RectTransform[] tabButtonRects = new RectTransform[5];
        readonly Image[] tabButtonImages = new Image[5];
        readonly Text[] tabButtonTexts = new Text[5];

        bool visible;
        float zoom = DefaultZoom;
        bool zoomInitialized;
        float screenSizePx;

        // Right-drag panning - the map no longer follows the player (see
        // Tick()), so this is the only way to look at a different part of the
        // map than whatever the fit-to-view default framed.
        Vector2 panOffset;
        bool dragging;
        Vector2 dragStartMouse;
        Vector2 dragStartPan;

        CursorLockMode savedLockState;
        bool savedCursorVisible;
        HotbarSlot savedEquippedSlot;
        Il2CppScheduleOne.Combat.PunchController punchController;

        static Font cachedFont;
        static bool loggedSpriteDiag;

        public FullMapView(RoutePlanner routePlanner)
        {
            this.routePlanner = routePlanner;
        }

        public void Tick()
        {
            // KeyCode.Hash (Unity's legacy-Input code for '#') doesn't reliably
            // arrive under Proton/Wine on non-US keyboard layouts (same class of
            // issue as GRAVE previously not registering for the dictation
            // hotkey - see session notes). Input.inputString reflects the
            // OS-translated typed character instead of a raw scancode, so it
            // isn't affected by that translation gap; check both.
            if (Input.GetKeyDown(KeyCode.Hash) || Input.inputString.IndexOf('#') >= 0)
                SetVisible(!visible);

            if (!visible)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetVisible(false);
                return;
            }

            // Defensive per-frame reassertion - see the comment in
            // SetVisible() on why a single SetPunchingEnabled(false) at open
            // time might not be enough.
            if (punchController != null)
                punchController.PunchingEnabled = false;

            Player player = Player.Local;
            MapPositionUtility posUtil = MapPositionUtility.Instance;
            MapApp mapApp = MapApp.Instance;
            if (player == null || posUtil == null || mapApp == null)
                return;

            EnsureMapSprite(mapApp, posUtil);
            HandleDrag();

            mapContainer.localScale = new Vector3(zoom, zoom, 1f);
            // zoom is fixed (fit-to-view, computed once in EnsureMapSprite).
            // Scroll-wheel zoom was removed, but that did NOT stop the mouse
            // wheel from cycling the equipped hotbar item while the tablet is
            // open (confirmed in testing) - that's the game's own scroll
            // binding still firing independently of our map, not something
            // our own (now-removed) zoom feature was causing or can fix by
            // itself. Needs a proper look at actually blocking that input
            // (still open). Right-drag panning (HandleDrag) is the only way to
            // look around the map for now. mapContainer.anchoredPosition =
            // panOffset - the map's own center sits at the viewport's center
            // by default (panOffset starts at zero each time the tablet
            // opens), shifted by however far the player has dragged since.
            mapContainer.anchoredPosition = panOffset;

            Vector2 playerMapPos = posUtil.GetMapPosition(ScheduleOneNavigatorMod.GetTrackedPosition(player));
            UpdatePlayerDot(playerMapPos);
            UpdateRoute(posUtil);

            otherPlayerRefreshTimer -= Time.unscaledDeltaTime;
            if (otherPlayerRefreshTimer <= 0f)
            {
                otherPlayerRefreshTimer = OtherPlayerRefreshInterval;
                RefreshOtherPlayerMarkers();
            }
            UpdateOtherPlayerMarkers(posUtil);

            // Only the active tab's markers should actually be visible on the
            // map - otherwise switching tabs would leave the previous tab's
            // dots cluttering the view alongside the new one.
            if (viewMode == MapViewMode.Deals)
            {
                dealRefreshTimer -= Time.unscaledDeltaTime;
                if (dealRefreshTimer <= 0f)
                {
                    dealRefreshTimer = DealRefreshInterval;
                    RefreshDeals(); // also refreshes customerMarkers, see RefreshDeals
                }
                UpdateCustomerMarkers(posUtil);
            }
            else
            {
                HideAllMarkers(customerMarkers);
            }

            if (viewMode == MapViewMode.Shops)
            {
                shopRefreshTimer -= Time.unscaledDeltaTime;
                if (shopRefreshTimer <= 0f)
                {
                    shopRefreshTimer = ShopRefreshInterval;
                    RefreshShops();
                }
                UpdateShopMarkers(posUtil);
            }
            else
            {
                HideAllMarkers(shopMarkers);
            }

            if (viewMode == MapViewMode.Businesses)
            {
                businessRefreshTimer -= Time.unscaledDeltaTime;
                if (businessRefreshTimer <= 0f)
                {
                    businessRefreshTimer = BusinessRefreshInterval;
                    RefreshBusinessMarkers();
                    RefreshBusinessList();
                }
                UpdateBusinessMarkers(posUtil);
            }
            else
            {
                HideAllMarkers(businessMarkers);
            }

            if (viewMode == MapViewMode.Dealers)
            {
                dealerRefreshTimer -= Time.unscaledDeltaTime;
                if (dealerRefreshTimer <= 0f)
                {
                    dealerRefreshTimer = DealerRefreshInterval;
                    RefreshDealerMarkers();
                    RefreshDealerList();
                }
                UpdateDealerMarkers(posUtil);
            }
            else
            {
                HideAllMarkers(dealerMarkers);
            }

            if (viewMode == MapViewMode.Properties)
            {
                propertyRefreshTimer -= Time.unscaledDeltaTime;
                if (propertyRefreshTimer <= 0f)
                {
                    propertyRefreshTimer = PropertyRefreshInterval;
                    RefreshPropertyMarkers();
                    RefreshPropertyList();
                }
                UpdatePropertyMarkers(posUtil);
            }
            else
            {
                HideAllMarkers(propertyMarkers);
            }

            // Back to left click (2026-09-19) - middle click was a working
            // sidestep for left-click-also-attacks (four targeted fixes
            // before it: Equip clearing, PunchController, GraphicRaycaster,
            // all confirmed live to do nothing), but Phone.ActiveApp (set in
            // SetVisible - see its comment) is a much likelier candidate for
            // the game's actual "is an app/menu open" gate, since the native
            // phone app doesn't have this problem and nothing we'd tried
            // before touched that field at all. Revert to middle click (see
            // session notes 2026-09-19) if this turns out not to fix it.
            if (Input.GetMouseButtonDown(0))
            {
                // Diagnostics (2026-09-19) - capture punch-related state at
                // the exact moment of a map click, to see whether
                // PunchingEnabled has already flipped back to true by then
                // (would mean something re-enables it every frame, possibly
                // after our own Tick() reassertion runs) or whether a punch
                // is somehow executing despite it staying false.
                if (punchController != null)
                    MelonLogger.Msg($"[Minimap] FullMapView: punch-block diag on click - " +
                        $"PunchingEnabled={punchController.PunchingEnabled}, IsPunching={punchController.IsPunching}, " +
                        $"IsLoading={punchController.IsLoading}");
                if (PlayerInventory.InstanceExists)
                    MelonLogger.Msg($"[Minimap] FullMapView: equip diag on click - " +
                        $"isAnythingEquipped={PlayerInventory.Instance.isAnythingEquipped}, " +
                        $"EquippedItem={(PlayerInventory.Instance.EquippedItem != null ? PlayerInventory.Instance.EquippedItem.ToString() : "null")}");

                Vector2 mousePos = Input.mousePosition;

                int clickedTab = FindClickedTab(mousePos);
                if (clickedTab >= 0)
                {
                    SetViewMode((MapViewMode)clickedTab);
                }
                else if (viewMode == MapViewMode.Deals)
                {
                    DealInfo clickedDealRow = FindClickedDealRow(mousePos);
                    if (clickedDealRow != null)
                    {
                        MelonLogger.Msg($"[Minimap] FullMapView: deal row clicked -> {clickedDealRow.CustomerName}");
                        routePlanner.SetDestination(clickedDealRow.DeliveryPosition);
                    }
                    else if (RectTransformUtility.RectangleContainsScreenPoint(routeButtonRect, mousePos))
                    {
                        HandleOptimalRouteButtonClicked(player);
                    }
                    else if (!dragging && RectTransformUtility.RectangleContainsScreenPoint(viewport, mousePos))
                    {
                        Customer clickedCustomer = FindClickedCustomer(mousePos);
                        if (clickedCustomer != null)
                        {
                            MelonLogger.Msg($"[Minimap] FullMapView: customer marker clicked -> {clickedCustomer.name}");
                            routePlanner.SetDestination(clickedCustomer.transform.position);
                        }
                        else
                        {
                            HandleClick(posUtil, player, playerMapPos);
                        }
                    }
                }
                else if (viewMode == MapViewMode.Shops)
                {
                    Transform clickedShopRow = FindClickedShopRow(mousePos);
                    if (clickedShopRow != null)
                    {
                        MelonLogger.Msg($"[Minimap] FullMapView: shop row clicked -> {clickedShopRow.position}");
                        routePlanner.SetDestination(clickedShopRow.position);
                    }
                    else if (!dragging && RectTransformUtility.RectangleContainsScreenPoint(viewport, mousePos))
                    {
                        Transform clickedShop = FindClickedShop(mousePos);
                        if (clickedShop != null)
                        {
                            MelonLogger.Msg($"[Minimap] FullMapView: shop marker clicked -> {clickedShop.position}");
                            routePlanner.SetDestination(clickedShop.position);
                        }
                        else
                        {
                            HandleClick(posUtil, player, playerMapPos);
                        }
                    }
                }
                else if (viewMode == MapViewMode.Businesses)
                {
                    Business clickedBusinessRow = FindClickedBusinessRow(mousePos);
                    if (clickedBusinessRow != null)
                    {
                        MelonLogger.Msg($"[Minimap] FullMapView: business row clicked -> {clickedBusinessRow.PropertyName}");
                        routePlanner.SetDestination(clickedBusinessRow.transform.position);
                    }
                    else if (!dragging && RectTransformUtility.RectangleContainsScreenPoint(viewport, mousePos))
                    {
                        Business clickedBusiness = FindClickedBusiness(mousePos);
                        if (clickedBusiness != null)
                        {
                            MelonLogger.Msg($"[Minimap] FullMapView: business marker clicked -> {clickedBusiness.PropertyName}");
                            routePlanner.SetDestination(clickedBusiness.transform.position);
                        }
                        else
                        {
                            HandleClick(posUtil, player, playerMapPos);
                        }
                    }
                }
                else if (viewMode == MapViewMode.Dealers)
                {
                    NPC clickedDealerRow = FindClickedDealerRow(mousePos);
                    if (clickedDealerRow != null)
                    {
                        MelonLogger.Msg($"[Minimap] FullMapView: dealer row clicked -> {clickedDealerRow.FullName}");
                        routePlanner.SetDestination(clickedDealerRow.transform.position);
                    }
                    else if (!dragging && RectTransformUtility.RectangleContainsScreenPoint(viewport, mousePos))
                    {
                        NPC clickedDealer = FindClickedDealer(mousePos);
                        if (clickedDealer != null)
                        {
                            MelonLogger.Msg($"[Minimap] FullMapView: dealer marker clicked -> {clickedDealer.FullName}");
                            routePlanner.SetDestination(clickedDealer.transform.position);
                        }
                        else
                        {
                            HandleClick(posUtil, player, playerMapPos);
                        }
                    }
                }
                else if (viewMode == MapViewMode.Properties)
                {
                    Property clickedPropertyRow = FindClickedPropertyRow(mousePos);
                    if (clickedPropertyRow != null)
                    {
                        MelonLogger.Msg($"[Minimap] FullMapView: property row clicked -> {clickedPropertyRow.PropertyName}");
                        routePlanner.SetDestination(clickedPropertyRow.transform.position);
                    }
                    else if (!dragging && RectTransformUtility.RectangleContainsScreenPoint(viewport, mousePos))
                    {
                        Property clickedProperty = FindClickedProperty(mousePos);
                        if (clickedProperty != null)
                        {
                            MelonLogger.Msg($"[Minimap] FullMapView: property marker clicked -> {clickedProperty.PropertyName}");
                            routePlanner.SetDestination(clickedProperty.transform.position);
                        }
                        else
                        {
                            HandleClick(posUtil, player, playerMapPos);
                        }
                    }
                }
                else if (!dragging && RectTransformUtility.RectangleContainsScreenPoint(viewport, mousePos))
                {
                    // No MapViewMode value falls through to here anymore -
                    // kept as a defensive fallback in case a future tab is
                    // added without its own branch yet.
                    HandleClick(posUtil, player, playerMapPos);
                }
            }
        }

        static void HideAllMarkers<TKey>(Dictionary<TKey, RectTransform> markers)
        {
            foreach (var kvp in markers)
                kvp.Value.gameObject.SetActive(false);
        }

        void HandleDrag()
        {
            if (Input.GetMouseButtonDown(1) &&
                RectTransformUtility.RectangleContainsScreenPoint(viewport, Input.mousePosition))
            {
                dragging = true;
                dragStartMouse = Input.mousePosition;
                dragStartPan = panOffset;
            }

            if (!dragging)
                return;

            if (Input.GetMouseButton(1))
            {
                // The canvas is ScreenSpaceOverlay with a plain CanvasScaler
                // (constant pixel size, factor 1), so a screen-pixel mouse
                // delta maps 1:1 onto UI anchoredPosition units - no further
                // conversion needed.
                Vector2 delta = (Vector2)Input.mousePosition - dragStartMouse;
                panOffset = dragStartPan + delta;
            }
            else
            {
                dragging = false;
            }
        }

        void HandleClick(MapPositionUtility posUtil, Player player, Vector2 playerMapPos)
        {
            Vector2 screenPoint = Input.mousePosition;

            // mapContainer's transform was already updated above this same
            // frame, before this check runs - so, unlike the old phone-map
            // click path, there is no risk of reading a stale/not-yet-rebuilt
            // layout here.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapContainer, screenPoint, null, out Vector2 clickedMapPos))
                return;

            if (!TryMapSpaceToWorld(posUtil, player, playerMapPos, clickedMapPos, out Vector3 world))
            {
                MelonLogger.Warning("[Minimap] FullMapView: could not calibrate map click -> world position.");
                return;
            }

            MelonLogger.Msg($"[Minimap] FullMapView click -> mapSpace {clickedMapPos}, world {world}, " +
                $"distFromPlayer {Vector3.Distance(ScheduleOneNavigatorMod.GetTrackedPosition(player), world):F1}m, zoom {zoom:F2}, " +
                $"playerMapPos {playerMapPos}, mapContainer.anchoredPosition {mapContainer.anchoredPosition}, " +
                $"mapContainer.sizeDelta {mapContainer.sizeDelta}");
            routePlanner.SetDestination(world);
            // Stays open (per user request) so the newly set route is visible
            // on the tablet immediately (see UpdateRoute) - close explicitly
            // with ESC or '#'.
        }

        // GetMapPosition() has proven (across many prior tests) to be a clean,
        // uniform-scale projection of world position with no warping - the
        // Jacobian sampled locally around the player via a small finite
        // difference is valid everywhere on the map, not just nearby. Since
        // clickedMapPos and playerMapPos are both already in GetMapPosition's
        // own coordinate space (see class comment), no origin-offset
        // workaround (like the old PlayerPoI-marker calibration) is needed
        // here - a plain difference against playerMapPos is correct.
        static bool TryMapSpaceToWorld(MapPositionUtility posUtil, Player player, Vector2 playerMapPos, Vector2 clickedMapPos, out Vector3 world)
        {
            world = default;
            const float SampleOffset = 50f;

            Vector3 origin = ScheduleOneNavigatorMod.GetTrackedPosition(player);
            Vector2 mapX = posUtil.GetMapPosition(origin + new Vector3(SampleOffset, 0f, 0f));
            Vector2 mapZ = posUtil.GetMapPosition(origin + new Vector3(0f, 0f, SampleOffset));

            Vector2 colX = (mapX - playerMapPos) / SampleOffset;
            Vector2 colZ = (mapZ - playerMapPos) / SampleOffset;

            float det = colX.x * colZ.y - colZ.x * colX.y;
            if (Mathf.Abs(det) < 1e-6f)
                return false;

            Vector2 rel = clickedMapPos - playerMapPos;
            float dxw = (rel.x * colZ.y - colZ.x * rel.y) / det;
            float dzw = (colX.x * rel.y - rel.x * colX.y) / det;

            // The map is a flat image - there's no real 3D-scene raycast for
            // a click on it, so the height has to be looked up separately.
            // Falling back to the player's own current height (origin.y)
            // only when that lookup fails - as a permanent placeholder, this
            // gave TerrainGrid's tight (2.5m) height-matching snap search
            // increasingly wrong data the farther/more different in
            // elevation the click was from the player's current spot,
            // causing far clicks to snap onto a coincidentally
            // similar-height but unrelated (and often isolated) cell, or
            // fail to snap at all - see the 2026-09-18 "deal list routes
            // fine, map click doesn't" finding that traced this down.
            float worldX = origin.x + dxw, worldZ = origin.z + dzw;
            float worldY = TerrainGrid.TryGetGroundHeight(worldX, worldZ, out float groundY) ? groundY : origin.y;
            world = new Vector3(worldX, worldY, worldZ);
            return true;
        }

        void EnsureMapSprite(MapApp mapApp, MapPositionUtility posUtil)
        {
            Sprite sprite = mapApp.MainMapSprite;
            if (sprite == null || mapImage.sprite == sprite)
                return;
            mapImage.sprite = sprite;
            mapContainer.sizeDelta = new Vector2(posUtil.MapDimensions, posUtil.MapDimensions);

            if (!loggedSpriteDiag)
            {
                loggedSpriteDiag = true;
                MelonLogger.Msg($"[Minimap] FullMapView sprite diag: sprite.rect={sprite.rect}, " +
                    $"sprite.texture=({sprite.texture.width}x{sprite.texture.height}), " +
                    $"MapDimensions={posUtil.MapDimensions}, mapContainer.sizeDelta={mapContainer.sizeDelta}");
            }

            // Default zoom fits the whole MapDimensions-sized canvas exactly
            // into the viewport, so opening the tablet shows the entire map at
            // a glance regardless of where the player currently stands -
            // scroll to zoom in from there.
            if (!zoomInitialized)
            {
                zoomInitialized = true;
                zoom = Mathf.Clamp(screenSizePx / posUtil.MapDimensions, MinZoom, MaxZoom);
            }
        }

        // Player position is drawn as a separate marker instead of being
        // implied by the view's own center (see Tick()) - clamped to the
        // viewport edge, the same way out-of-range markers work on the small
        // minimap, so it stays visible even panned/zoomed away from the
        // player's actual position.
        void UpdatePlayerDot(Vector2 playerMapPos)
        {
            Vector2 pos = playerMapPos * zoom + panOffset;
            float half = screenSizePx / 2f - PlayerDotSize / 2f;
            pos.x = Mathf.Clamp(pos.x, -half, half);
            pos.y = Mathf.Clamp(pos.y, -half, half);
            playerDot.anchoredPosition = pos;
        }

        const float RouteDotSpacingMeters = 5.75f; // 5f, +15% per user request (denser dots looked "crooked")

        // Draws the active route (if any) on the tablet: a dot every fixed
        // world-space distance along the straight segments between waypoints
        // (not one dot per waypoint - those land at very uneven spacing,
        // since simplified waypoints can be a meter or 100 meters apart,
        // which reads as "crooked" even though every segment between them
        // really is straight) plus a distinct marker at the final
        // destination. Interpolating in world space first, then projecting
        // each sample point through GetMapPosition, mirrors the small
        // minimap's own UpdateRouteDots exactly.
        void UpdateRoute(MapPositionUtility posUtil)
        {
            IReadOnlyList<Vector3> waypoints = routePlanner.Waypoints;
            int used = 0;
            if (routePlanner.HasRoute && waypoints.Count >= 2)
            {
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
                        Vector2 mapPos = posUtil.GetMapPosition(dotWorld);
                        RectTransform dot = GetOrCreateRouteDot(used);
                        dot.gameObject.SetActive(true);
                        dot.anchoredPosition = mapPos * zoom + panOffset;
                        used++;
                        posAlongSeg += RouteDotSpacingMeters;
                    }
                    distanceUntilNextDot = posAlongSeg - segLen;
                }
            }
            for (int i = used; i < routeDotPool.Count; i++)
                routeDotPool[i].gameObject.SetActive(false);

            if (routePlanner.HasDestination)
            {
                Vector2 destMapPos = posUtil.GetMapPosition(routePlanner.Destination);
                destinationDot.gameObject.SetActive(true);
                destinationDot.anchoredPosition = destMapPos * zoom + panOffset;
            }
            else
            {
                destinationDot.gameObject.SetActive(false);
            }
        }

        // Only customers with a currently active deal (i.e. exactly the
        // customers in currentDeals/the deal list) - was originally all of
        // Customer.UnlockedCustomers (every discovered customer, deal or
        // not), which the user found confusing (dot count on the map didn't
        // match the deal list count); narrowed to match on 2026-09-19. Called
        // from RefreshDeals() right after currentDeals is rebuilt, not on its
        // own timer, so the two never disagree for a frame.
        void RefreshCustomerMarkers()
        {
            HashSet<Customer> withDeal = new HashSet<Customer>();
            foreach (DealInfo deal in currentDeals)
                if (deal.Customer != null)
                    withDeal.Add(deal.Customer);

            List<Customer> stale = null;
            foreach (var kvp in customerMarkers)
                if (kvp.Key == null || !withDeal.Contains(kvp.Key))
                    (stale ??= new List<Customer>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(customerMarkers[key].gameObject);
                    customerMarkers.Remove(key);
                }

            foreach (Customer customer in withDeal)
            {
                if (customerMarkers.ContainsKey(customer))
                    continue;

                GameObject go = new GameObject("CustomerMarker");
                go.transform.SetParent(viewport, false);
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(CustomerDotSize, CustomerDotSize);
                go.AddComponent<Image>().sprite = customerSprite;
                customerMarkers[customer] = rt;
            }
        }

        void UpdateCustomerMarkers(MapPositionUtility posUtil)
        {
            foreach (var kvp in customerMarkers)
            {
                Customer customer = kvp.Key;
                RectTransform rt = kvp.Value;
                if (customer == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 mapPos = posUtil.GetMapPosition(customer.transform.position);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = mapPos * zoom + panOffset;
            }
        }

        // Hit-tests a screen point against every currently visible customer
        // marker (with a bit of extra click padding, since the dots
        // themselves are small) and returns the closest match, or null.
        // Checked before falling back to a plain "set destination here" map
        // click, so clicking near a customer navigates straight to them.
        Customer FindClickedCustomer(Vector2 screenPoint)
        {
            Customer best = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in customerMarkers)
            {
                RectTransform rt = kvp.Value;
                if (!rt.gameObject.activeSelf)
                    continue;
                Vector2 markerScreenPos = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                float dist = Vector2.Distance(screenPoint, markerScreenPos);
                float hitRadius = CustomerDotSize / 2f + CustomerClickPadding;
                if (dist <= hitRadius && dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Key;
                }
            }
            return best;
        }

        // Businesses tab - same shape as RefreshCustomerMarkers/
        // UpdateCustomerMarkers/FindClickedCustomer above, sourced from
        // Business.Businesses (every business in the scene, not just
        // player-owned ones). Business (a physically-placed NetworkBehaviour,
        // same precedent as Property.OwnedProperties/Business.OwnedBusinesses
        // in ScheduleOneNavigator.cs) has a real, distinct world position per instance,
        // unlike ShopInterface (a UI-only panel with no world position of its
        // own - see the Shops tab below, and session notes from 2026-09-18).
        void RefreshBusinessMarkers()
        {
            var allBusinesses = Business.Businesses;
            HashSet<Business> current = new HashSet<Business>();
            if (allBusinesses != null)
                foreach (Business b in allBusinesses)
                    if (b != null)
                        current.Add(b);

            List<Business> stale = null;
            foreach (var kvp in businessMarkers)
                if (kvp.Key == null || !current.Contains(kvp.Key))
                    (stale ??= new List<Business>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(businessMarkers[key].gameObject);
                    businessMarkers.Remove(key);
                }

            foreach (Business business in current)
            {
                if (businessMarkers.ContainsKey(business))
                    continue;

                GameObject go = new GameObject("BusinessMarker");
                go.transform.SetParent(viewport, false);
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(BusinessDotSize, BusinessDotSize);
                go.AddComponent<Image>().sprite = businessSprite;
                businessMarkers[business] = rt;
            }
        }

        void UpdateBusinessMarkers(MapPositionUtility posUtil)
        {
            foreach (var kvp in businessMarkers)
            {
                Business business = kvp.Key;
                RectTransform rt = kvp.Value;
                if (business == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 mapPos = posUtil.GetMapPosition(business.transform.position);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = mapPos * zoom + panOffset;
            }
        }

        Business FindClickedBusiness(Vector2 screenPoint)
        {
            Business best = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in businessMarkers)
            {
                RectTransform rt = kvp.Value;
                if (!rt.gameObject.activeSelf)
                    continue;
                Vector2 markerScreenPos = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                float dist = Vector2.Distance(screenPoint, markerScreenPos);
                float hitRadius = BusinessDotSize / 2f + BusinessClickPadding;
                if (dist <= hitRadius && dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Key;
                }
            }
            return best;
        }

        // True for an NPC belonging to the 6-character "Dealer" hierarchy
        // (own ShopInterface field per class - see RefreshShops), as opposed
        // to the separate "Supplier" hierarchy below. Split into two checks
        // (instead of one combined bool) so the Dealers tab can group by
        // category, not just exclude from Shops.
        static bool IsDealerNpc(NPC npc)
        {
            return npc.TryCast<Stan>() != null
                || npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Dan>() != null
                || npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Fiona>() != null
                || npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Herbert>() != null
                || npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Oscar>() != null
                || npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Steve>() != null;
        }

        // True for an NPC belonging to the Supplier : NPC hierarchy
        // (Albert/Phil/Salvador/Shirley - see RefreshShops).
        static bool IsSupplierNpc(NPC npc)
        {
            return npc.TryCast<Supplier>() != null;
        }

        // True for any NPC belonging to either hierarchy - excluded from the
        // Shops tab (see RefreshShops) and shown on the Dealers tab (see
        // RefreshDealerList/RefreshDealerMarkers below) - shared so the two
        // lists ("not a shop" / "is a dealer or supplier") can never drift
        // apart.
        static bool IsDealerOrSupplierNpc(NPC npc)
        {
            return IsDealerNpc(npc) || IsSupplierNpc(npc);
        }

        // Dealers tab - same shape as RefreshBusinessMarkers/
        // UpdateBusinessMarkers/FindClickedBusiness above, sourced from
        // NPCManager.NPCRegistry filtered to dealer/supplier NPCs (see
        // IsDealerOrSupplierNpc). Both are plain NPC : NetworkBehaviour, so
        // .transform.position/.FullName work directly - no position-source
        // guesswork needed like on the Shops tab, EXCEPT that a
        // not-yet-spawned NPC's transform can sit at a meaningless
        // placeholder position (confirmed live 2026-09-19: Albert Hoover
        // shown far off in the wilderness) - activeInHierarchy is the
        // standard Unity signal that a GameObject isn't currently part of
        // the live scene, so such NPCs are excluded entirely (no marker, no
        // row) rather than shown at a possibly-wrong spot.
        void RefreshDealerMarkers()
        {
            var npcs = NPCManager.NPCRegistry;
            HashSet<NPC> current = new HashSet<NPC>();
            if (npcs != null)
                foreach (NPC npc in npcs)
                    if (npc != null && IsDealerOrSupplierNpc(npc) && npc.gameObject.activeInHierarchy)
                        current.Add(npc);

            List<NPC> stale = null;
            foreach (var kvp in dealerMarkers)
                if (kvp.Key == null || !current.Contains(kvp.Key))
                    (stale ??= new List<NPC>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(dealerMarkers[key].gameObject);
                    dealerMarkers.Remove(key);
                }

            foreach (NPC npc in current)
            {
                bool available = npc.RelationData != null && npc.RelationData.Unlocked;

                if (!dealerMarkers.TryGetValue(npc, out RectTransform rt))
                {
                    GameObject go = new GameObject("DealerMarker");
                    go.transform.SetParent(viewport, false);
                    rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(DealerDotSize, DealerDotSize);
                    go.AddComponent<Image>();
                    dealerMarkers[npc] = rt;
                }

                // Sprite reflects RelationData.Unlocked, refreshed every
                // cycle (not just on creation) - a dealer/supplier can become
                // available mid-session as the player progresses.
                rt.GetComponent<Image>().sprite = available ? dealerSprite : dealerSpriteLocked;
            }
        }

        void UpdateDealerMarkers(MapPositionUtility posUtil)
        {
            foreach (var kvp in dealerMarkers)
            {
                NPC npc = kvp.Key;
                RectTransform rt = kvp.Value;
                if (npc == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 mapPos = posUtil.GetMapPosition(npc.transform.position);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = mapPos * zoom + panOffset;
            }
        }

        NPC FindClickedDealer(Vector2 screenPoint)
        {
            NPC best = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in dealerMarkers)
            {
                RectTransform rt = kvp.Value;
                if (!rt.gameObject.activeSelf)
                    continue;
                Vector2 markerScreenPos = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                float dist = Vector2.Distance(screenPoint, markerScreenPos);
                float hitRadius = DealerDotSize / 2f + DealerClickPadding;
                if (dist <= hitRadius && dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Key;
                }
            }
            return best;
        }

        // Properties tab - only owned properties get a map marker (unowned
        // ones are still listed, see RefreshPropertyList, just without a
        // marker/route - "gray and inert" per the user's request 2026-09-19).
        void RefreshPropertyMarkers()
        {
            var allProperties = Property.Properties;
            HashSet<Property> owned = new HashSet<Property>();
            if (allProperties != null)
                foreach (Property p in allProperties)
                    if (p != null && p.IsOwned)
                        owned.Add(p);

            List<Property> stale = null;
            foreach (var kvp in propertyMarkers)
                if (kvp.Key == null || !owned.Contains(kvp.Key))
                    (stale ??= new List<Property>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(propertyMarkers[key].gameObject);
                    propertyMarkers.Remove(key);
                }

            foreach (Property p in owned)
            {
                if (propertyMarkers.ContainsKey(p))
                    continue;

                GameObject go = new GameObject("PropertyMarker");
                go.transform.SetParent(viewport, false);
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(PropertyDotSize, PropertyDotSize);
                go.AddComponent<Image>().sprite = propertySprite;
                propertyMarkers[p] = rt;
            }
        }

        void UpdatePropertyMarkers(MapPositionUtility posUtil)
        {
            foreach (var kvp in propertyMarkers)
            {
                Property p = kvp.Key;
                RectTransform rt = kvp.Value;
                if (p == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 mapPos = posUtil.GetMapPosition(p.transform.position);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = mapPos * zoom + panOffset;
            }
        }

        Property FindClickedProperty(Vector2 screenPoint)
        {
            Property best = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in propertyMarkers)
            {
                RectTransform rt = kvp.Value;
                if (!rt.gameObject.activeSelf)
                    continue;
                Vector2 markerScreenPos = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                float dist = Vector2.Distance(screenPoint, markerScreenPos);
                float hitRadius = PropertyDotSize / 2f + PropertyClickPadding;
                if (dist <= hitRadius && dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Key;
                }
            }
            return best;
        }

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
            {
                if (otherPlayerMarkers.ContainsKey(p))
                    continue;

                GameObject go = new GameObject("OtherPlayerMarker");
                go.transform.SetParent(viewport, false);
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(OtherPlayerDotSize, OtherPlayerDotSize);
                go.AddComponent<Image>().sprite = otherPlayerSprite;
                otherPlayerMarkers[p] = rt;
            }
        }

        void UpdateOtherPlayerMarkers(MapPositionUtility posUtil)
        {
            foreach (var kvp in otherPlayerMarkers)
            {
                Player p = kvp.Key;
                RectTransform rt = kvp.Value;
                if (p == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 mapPos = posUtil.GetMapPosition(ScheduleOneNavigatorMod.GetTrackedPosition(p));
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = mapPos * zoom + panOffset;
            }
        }

        // Shops tab - real retail locations (Gas Mart, hardware stores, Auto
        // Shop, Pawn Shop, Ray's Realty, ...), distinct in-game from both
        // Deals/customers and Businesses (confirmed via a reference
        // companion-map screenshot, 2026-09-19). Unlike those, the game has
        // no single class/list that covers every Shop - confirmed via
        // decompile of Assembly-CSharp.dll:
        //   - ShopInterface (generic shop UI, ShopInterface.AllShops) covers
        //     most vendor shops (Gas Mart, hardware stores, clothing shops),
        //     but ShopInterface itself has no usable world position (see
        //     2026-09-18 fix) - its LoadingBayDetector/DeliveryBays fields
        //     are real, physically-placed scene components though.
        //   - A handful of shops are run by a single bespoke NPC class that
        //     exposes its own shop-panel reference - mirrors the known
        //     Dan/Stan/Fiona/Herbert/Oscar/Steve dealer-NPC pattern, but
        //     these ones (Jeremy = Auto Shop, Ray = Ray's Realty) are
        //     regular NPCs, not Dealers.
        //   - Pawn Shop is a singleton (PawnShopInterface.Instance) with a
        //     direct PawnShopNPC field.
        //   - Casino/Barbershop/Top Tattoos: no reliable position source was
        //     found via static analysis (BarbershopUI/TattooShopUI only
        //     reach a CharacterCustomizationShop via FindObjectsOfType, which
        //     is also used for the in-home wardrobe/mirror - too easy to
        //     misidentify; Casino has no wrapper class at all) - left out of
        //     the list entirely for now rather than showing a guessed/wrong
        //     marker.
        // Markers/rows are keyed by the real underlying Transform (whichever
        // of the above actually held that shop's position), not by a
        // shop-specific type, since there isn't one common type to key by.
        void RefreshShops()
        {
            currentShops.Clear();
            HashSet<Transform> current = new HashSet<Transform>();

            var npcs = NPCManager.NPCRegistry;

            // The 6 known dealer NPCs (Stan/Dan/Fiona/Herbert/Oscar/Steve)
            // each carry their own ShopInterface field, which shows up in
            // ShopInterface.AllShops right alongside genuine retail shops
            // (confirmed live 2026-09-19: "Oscar's Store" leaked into the
            // Shops tab this way). Dealers are their own, separate category
            // per the user - build the set of their ShopInterface instances
            // upfront so the generic sweep below can skip them.
            HashSet<ShopInterface> dealerShops = new HashSet<ShopInterface>();
            if (npcs != null)
                foreach (NPC dealerNpc in npcs)
                {
                    if (dealerNpc == null)
                        continue;
                    ShopInterface dealerShop =
                        dealerNpc.TryCast<Stan>()?.ShopInterface
                        ?? dealerNpc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Dan>()?.ShopInterface
                        ?? dealerNpc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Fiona>()?.ShopInterface
                        ?? dealerNpc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Herbert>()?.ShopInterface
                        ?? dealerNpc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Oscar>()?.ShopInterface
                        ?? dealerNpc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Steve>()?.ShopInterface
                        // Second dealer hierarchy: Supplier : NPC has its own
                        // Shop property, inherited by Albert/Phil/Salvador/
                        // Shirley (confirmed via decompile: exactly these 4
                        // classes extend Supplier in the whole assembly) -
                        // leaked into the Shops tab as unroutable entries
                        // until found live 2026-09-19 ("Salvador"/"Albert
                        // Hoover"/"Fungal Phil"/"Shirley Watts").
                        ?? dealerNpc.TryCast<Supplier>()?.Shop;
                    if (dealerShop != null)
                        dealerShops.Add(dealerShop);
                }

            var allShops = ShopInterface.AllShops;
            if (allShops != null)
                foreach (ShopInterface s in allShops)
                {
                    if (s == null || dealerShops.Contains(s))
                        continue;
                    // Fallback chain, most to least precise - the user wants
                    // every shop represented rather than silently dropping
                    // the ones without a delivery bay (Bleuballs
                    // Boutique/Shred Shack/Thrifty Threads/Warehouse, 2026-09-19),
                    // even though step 4 is known-unreliable for at least
                    // some shops (see the 2026-09-18 "all shops collapse to
                    // one point" finding) - better an approximate marker than
                    // a missing one, per explicit user instruction.
                    Transform pos = s.LoadingBayDetector != null
                        ? s.LoadingBayDetector.transform
                        : (s.DeliveryBays != null && s.DeliveryBays.Length > 0 && s.DeliveryBays[0] != null
                            ? s.DeliveryBays[0].transform
                            : (s.GetComponentInParent<Property>() != null
                                ? s.GetComponentInParent<Property>().transform
                                : s.transform));
                    pos = ApplyShopPositionOverride(s.ShopName, pos);
                    if (current.Add(pos))
                        currentShops.Add((pos, s.ShopName));
                }

            Il2CppScheduleOne.Map.Dealership jeremyDealership = null;
            if (npcs != null)
                foreach (NPC npc in npcs)
                {
                    if (npc == null)
                        continue;
                    Il2CppScheduleOne.NPCs.CharacterClasses.Jeremy jeremy = npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Jeremy>();
                    if (jeremy != null)
                    {
                        jeremyDealership = jeremy.Dealership;
                        Transform autoShopPos = ApplyShopPositionOverride("Auto Shop", jeremy.transform);
                        if (current.Add(autoShopPos))
                            currentShops.Add((autoShopPos, "Auto Shop"));
                        continue;
                    }
                    Il2CppScheduleOne.NPCs.CharacterClasses.Ray ray = npc.TryCast<Il2CppScheduleOne.NPCs.CharacterClasses.Ray>();
                    if (ray != null)
                    {
                        Transform rayPos = ApplyShopPositionOverride("Ray's Realty", ray.transform);
                        if (current.Add(rayPos))
                            currentShops.Add((rayPos, "Ray's Realty"));
                    }
                }

            var pawnShopInterface = Il2CppScheduleOne.UI.PawnShopInterface.Instance;
            NPC pawnShopNpc = pawnShopInterface != null ? pawnShopInterface.PawnShopNPC : null;
            if (pawnShopNpc != null)
            {
                Transform pawnShopPos = ApplyShopPositionOverride("Pawn Shop", pawnShopNpc.transform);
                if (current.Add(pawnShopPos))
                    currentShops.Add((pawnShopPos, "Pawn Shop"));
            }

            // Hyland Auto - a second car dealership, distinct from Jeremy's
            // own ("Auto Shop"). Dealership has no static registry and no
            // name field (confirmed via decompile), so every Dealership in
            // the scene has to be found at runtime and whichever isn't
            // Jeremy's is assumed to be Hyland Auto. If more than two
            // Dealership instances exist, all the extra ones would also be
            // labelled "Hyland Auto" - not distinguishable without a live
            // check, per the user's explicit "include everything" request.
            foreach (Il2CppScheduleOne.Map.Dealership dealership in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Map.Dealership>())
            {
                if (dealership == null || dealership == jeremyDealership)
                    continue;
                Transform pos = dealership.SpawnPoints != null && dealership.SpawnPoints.Length > 0 && dealership.SpawnPoints[0] != null
                    ? dealership.SpawnPoints[0].transform
                    : dealership.transform;
                pos = ApplyShopPositionOverride("Hyland Auto", pos);
                if (current.Add(pos))
                    currentShops.Add((pos, "Hyland Auto"));
            }

            // Barbershop / Top Tattoos - both reach a physically-placed
            // CharacterCustomizationShop (real CameraPosition/RigContainer
            // transforms) through their base class. No static registry
            // either. RigContainer preferred over CameraPosition - the
            // latter is a camera-framing point for the preview shot, not
            // necessarily a walkable ground position; routing to it failed
            // live for Top Tattoos on 2026-09-19 (RigContainer, where the
            // avatar preview actually stands, should be closer to the floor).
            foreach (Il2CppScheduleOne.UI.CharacterCustomization.BarbershopUI ui in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.UI.CharacterCustomization.BarbershopUI>())
            {
                if (ui == null)
                    continue;
                var shop = ui.CharacterCustomizationShop;
                Transform pos = shop != null ? (shop.RigContainer != null ? shop.RigContainer : shop.CameraPosition) : null;
                pos = ApplyShopPositionOverride("Barbershop", pos);
                if (pos == null)
                {
                    if (loggedMissingShopPositions.Add("Barbershop"))
                        LogMissingShopPosition("Barbershop", "CharacterCustomizationShop/CameraPosition/RigContainer all null", ui);
                    continue;
                }
                if (current.Add(pos))
                    currentShops.Add((pos, "Barbershop"));
            }

            foreach (Il2CppScheduleOne.UI.CharacterCustomization.TattooShopUI ui in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.UI.CharacterCustomization.TattooShopUI>())
            {
                if (ui == null)
                    continue;
                var shop = ui.CharacterCustomizationShop;
                Transform pos = shop != null ? (shop.RigContainer != null ? shop.RigContainer : shop.CameraPosition) : null;
                pos = ApplyShopPositionOverride("Top Tattoos", pos);
                if (pos == null)
                {
                    if (loggedMissingShopPositions.Add("Top Tattoos"))
                        LogMissingShopPosition("Top Tattoos", "CharacterCustomizationShop/CameraPosition/RigContainer all null", ui);
                    continue;
                }
                if (current.Add(pos))
                    currentShops.Add((pos, "Top Tattoos"));
            }

            // Casino - several CasinoGameInteraction table components exist,
            // one marker is enough; take the first one found.
            foreach (Il2CppScheduleOne.Casino.CasinoGameInteraction table in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Casino.CasinoGameInteraction>())
            {
                if (table == null)
                    continue;
                Transform casinoPos = ApplyShopPositionOverride("Casino", table.transform);
                if (current.Add(casinoPos))
                    currentShops.Add((casinoPos, "Casino"));
                break;
            }

            // Markers: same add/remove-by-diff shape as customer/business
            // markers above.
            List<Transform> stale = null;
            foreach (var kvp in shopMarkers)
                if (kvp.Key == null || !current.Contains(kvp.Key))
                    (stale ??= new List<Transform>()).Add(kvp.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    GameObject.Destroy(shopMarkers[key].gameObject);
                    shopMarkers.Remove(key);
                }

            foreach (var entry in currentShops)
            {
                if (shopMarkers.ContainsKey(entry.position))
                    continue;

                GameObject go = new GameObject("ShopMarker");
                go.transform.SetParent(viewport, false);
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(ShopDotSize, ShopDotSize);
                go.AddComponent<Image>().sprite = shopSprite;
                shopMarkers[entry.position] = rt;
            }

            // Row list - same destroy/rebuild-every-refresh shape as
            // RefreshDeals/RefreshBusinessList.
            foreach (var row in shopRows)
                GameObject.Destroy(row.rect.gameObject);
            shopRows.Clear();

            for (int i = 0; i < currentShops.Count; i++)
            {
                (Transform position, string name) = currentShops[i];

                GameObject rowGO = new GameObject("ShopRow");
                rowGO.transform.SetParent(dealListContainer, false);
                RectTransform rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(1f, 1f);
                rowRT.pivot = new Vector2(0.5f, 1f);
                rowRT.anchoredPosition = new Vector2(0f, -i * ShopRowHeight);
                rowRT.sizeDelta = new Vector2(0f, ShopRowHeight - 4f);
                rowGO.AddComponent<Image>().color = ShopRowColor;

                GameObject nameGO = new GameObject("Name");
                nameGO.transform.SetParent(rowGO.transform, false);
                RectTransform nameRT = nameGO.AddComponent<RectTransform>();
                nameRT.anchorMin = Vector2.zero;
                nameRT.anchorMax = Vector2.one;
                nameRT.offsetMin = new Vector2(8f, 0f);
                nameRT.offsetMax = new Vector2(-8f, 0f);
                Text nameText = nameGO.AddComponent<Text>();
                nameText.font = GetFont();
                nameText.text = name;
                nameText.fontSize = 15;
                nameText.color = DealRowTextColor;
                nameText.alignment = TextAnchor.MiddleLeft;
                nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                nameText.verticalOverflow = VerticalWrapMode.Truncate;

                shopRows.Add((rowRT, position, name));
            }

            dealListContainer.sizeDelta = new Vector2(0f, currentShops.Count * ShopRowHeight);
        }

        // Extra diagnostics for a shop RefreshShops() couldn't place at all
        // (no known component with a usable world position) - dumps the
        // parent-transform chain and the component list on the given
        // component's own GameObject, so a live log capture can point at a
        // usable position source next time instead of another round of
        // guessing.
        static void LogMissingShopPosition(string shopName, string reason, Component c)
        {
            var parentNames = new List<string>();
            Transform t = c.transform.parent;
            for (int i = 0; i < 5 && t != null; i++)
            {
                parentNames.Add(t.name);
                t = t.parent;
            }

            var componentNames = new List<string>();
            foreach (Component own in c.gameObject.GetComponents<Component>())
                if (own != null)
                    componentNames.Add(own.GetType().Name);

            MelonLogger.Warning($"[Minimap] FullMapView: shop '{shopName}' - {reason} - no known world position, skipping its marker. " +
                $"parentChain=[{string.Join(" -> ", parentNames)}], ownComponents=[{string.Join(", ", componentNames)}]");
        }

        void UpdateShopMarkers(MapPositionUtility posUtil)
        {
            foreach (var kvp in shopMarkers)
            {
                Transform position = kvp.Key;
                RectTransform rt = kvp.Value;
                if (position == null)
                {
                    rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 mapPos = posUtil.GetMapPosition(position.position);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = mapPos * zoom + panOffset;
            }
        }

        Transform FindClickedShop(Vector2 screenPoint)
        {
            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (var kvp in shopMarkers)
            {
                RectTransform rt = kvp.Value;
                if (!rt.gameObject.activeSelf)
                    continue;
                Vector2 markerScreenPos = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                float dist = Vector2.Distance(screenPoint, markerScreenPos);
                float hitRadius = ShopDotSize / 2f + ShopClickPadding;
                if (dist <= hitRadius && dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Key;
                }
            }
            return best;
        }

        Transform FindClickedShopRow(Vector2 screenPoint)
        {
            foreach (var row in shopRows)
                if (row.rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(row.rect, screenPoint))
                    return row.position;
            return null;
        }

        // Rebuilds the deal list every DealRefreshInterval - deal counts are
        // always small (a handful at most) so destroying and recreating the
        // rows each time is simpler than pooling/diffing, and cheap enough at
        // 1Hz. Grouped into "Aktuell" (customer standing by, ready for
        // handover) and "Geplant" (contract accepted/claimed with a delivery
        // location, but not due yet - e.g. auto-claimed and scheduled by the
        // Dealcraft mod into a future time window) since 2026-09-19; before
        // that, DealPlanner only ever returned the "Aktuell" ones, so a
        // scheduled-but-not-yet-due deal was invisible on the tablet even
        // though it was a real, active contract.
        void RefreshDeals()
        {
            currentDeals.Clear();
            currentDeals.AddRange(DealPlanner.GatherActiveDeals());

            foreach (var row in dealRows)
                GameObject.Destroy(row.rect.gameObject);
            dealRows.Clear();
            foreach (var header in dealHeaderRows)
                GameObject.Destroy(header.gameObject);
            dealHeaderRows.Clear();

            var currentGroup = new List<DealInfo>();
            var plannedGroup = new List<DealInfo>();
            foreach (DealInfo deal in currentDeals)
                (deal.AwaitingDelivery ? currentGroup : plannedGroup).Add(deal);

            float y = 0f;

            void AddHeader(string label)
            {
                GameObject headerGO = new GameObject("DealHeader");
                headerGO.transform.SetParent(dealListContainer, false);
                RectTransform headerRT = headerGO.AddComponent<RectTransform>();
                headerRT.anchorMin = new Vector2(0f, 1f);
                headerRT.anchorMax = new Vector2(1f, 1f);
                headerRT.pivot = new Vector2(0.5f, 1f);
                headerRT.anchoredPosition = new Vector2(0f, -y);
                headerRT.sizeDelta = new Vector2(0f, SectionHeaderRowHeight);
                headerGO.AddComponent<Image>().color = SectionHeaderRowColor;

                GameObject textGO = new GameObject("Text");
                textGO.transform.SetParent(headerGO.transform, false);
                RectTransform textRT = textGO.AddComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(8f, 0f);
                textRT.offsetMax = new Vector2(-8f, 0f);
                Text text = textGO.AddComponent<Text>();
                text.font = GetFont();
                text.text = label;
                text.fontSize = 13;
                text.fontStyle = FontStyle.Bold;
                text.color = SectionHeaderTextColor;
                text.alignment = TextAnchor.MiddleLeft;

                dealHeaderRows.Add(headerRT);
                y += SectionHeaderRowHeight;
            }

            void AddRow(DealInfo deal)
            {
                GameObject rowGO = new GameObject("DealRow");
                rowGO.transform.SetParent(dealListContainer, false);
                RectTransform rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(1f, 1f);
                rowRT.pivot = new Vector2(0.5f, 1f);
                rowRT.anchoredPosition = new Vector2(0f, -y);
                rowRT.sizeDelta = new Vector2(0f, DealRowHeight - 4f);
                rowGO.AddComponent<Image>().color = deal.Feasible ? DealRowFeasibleColor : DealRowInfeasibleColor;

                GameObject nameGO = new GameObject("Name");
                nameGO.transform.SetParent(rowGO.transform, false);
                RectTransform nameRT = nameGO.AddComponent<RectTransform>();
                nameRT.anchorMin = new Vector2(0f, 0.55f);
                nameRT.anchorMax = new Vector2(1f, 1f);
                nameRT.offsetMin = new Vector2(8f, 0f);
                nameRT.offsetMax = new Vector2(-8f, -2f);
                Text nameText = nameGO.AddComponent<Text>();
                nameText.font = GetFont();
                nameText.text = $"{deal.CustomerName}  -  ${deal.Payment:F0}";
                nameText.fontSize = 15;
                nameText.color = DealRowTextColor;
                nameText.alignment = TextAnchor.MiddleLeft;
                nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                nameText.verticalOverflow = VerticalWrapMode.Truncate;

                GameObject prodGO = new GameObject("Product");
                prodGO.transform.SetParent(rowGO.transform, false);
                RectTransform prodRT = prodGO.AddComponent<RectTransform>();
                prodRT.anchorMin = new Vector2(0f, 0.15f);
                prodRT.anchorMax = new Vector2(1f, 0.55f);
                prodRT.offsetMin = new Vector2(8f, 0f);
                prodRT.offsetMax = new Vector2(-8f, 0f);
                Text prodText = prodGO.AddComponent<Text>();
                prodText.font = GetFont();
                prodText.text = deal.ProductSummary;
                prodText.fontSize = 12;
                prodText.color = DealRowSubTextColor;
                prodText.alignment = TextAnchor.MiddleLeft;
                prodText.horizontalOverflow = HorizontalWrapMode.Wrap;
                prodText.verticalOverflow = VerticalWrapMode.Truncate;

                GameObject tagGO = new GameObject("Tag");
                tagGO.transform.SetParent(rowGO.transform, false);
                RectTransform tagRT = tagGO.AddComponent<RectTransform>();
                tagRT.anchorMin = new Vector2(0f, 0f);
                tagRT.anchorMax = new Vector2(1f, 0.15f);
                tagRT.offsetMin = new Vector2(8f, 0f);
                tagRT.offsetMax = new Vector2(-8f, 0f);
                Text tagText = tagGO.AddComponent<Text>();
                tagText.font = GetFont();
                tagText.text = deal.Feasible ? "erfüllbar" : "fehlt Ware";
                tagText.fontSize = 11;
                tagText.color = deal.Feasible ? DealFeasibleTagColor : DealInfeasibleTagColor;
                tagText.alignment = TextAnchor.MiddleLeft;

                dealRows.Add((rowRT, deal));
                y += DealRowHeight;
            }

            if (currentGroup.Count > 0)
            {
                AddHeader("Aktuell");
                foreach (DealInfo deal in currentGroup)
                    AddRow(deal);
            }
            if (plannedGroup.Count > 0)
            {
                AddHeader("Geplant");
                foreach (DealInfo deal in plannedGroup)
                    AddRow(deal);
            }

            dealListContainer.sizeDelta = new Vector2(0f, y);
            UpdateRouteButtonVisual();
            RefreshCustomerMarkers();
        }

        DealInfo FindClickedDealRow(Vector2 screenPoint)
        {
            foreach (var row in dealRows)
                if (row.rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(row.rect, screenPoint))
                    return row.deal;
            return null;
        }

        void UpdateRouteButtonVisual()
        {
            // Planned (not-yet-awaiting) deals are excluded here - routing
            // to a delivery that isn't due yet is premature, this button is
            // specifically "go fulfill something right now".
            int feasibleCount = 0;
            foreach (var d in currentDeals)
                if (d.Feasible && d.AwaitingDelivery)
                    feasibleCount++;

            routeButtonImage.color = feasibleCount > 0 ? RouteButtonColor : RouteButtonDisabledColor;
            routeButtonText.text = feasibleCount > 0
                ? $"Optimale Route ({feasibleCount})"
                : "Optimale Route (keine Ware)";
        }

        void HandleOptimalRouteButtonClicked(Player player)
        {
            var feasible = new List<DealInfo>();
            foreach (var d in currentDeals)
                if (d.Feasible && d.AwaitingDelivery)
                    feasible.Add(d);

            if (feasible.Count == 0)
            {
                MelonLogger.Msg("[Minimap] FullMapView: optimal-route button clicked, but no deal is currently fulfillable with the inventory on hand.");
                return;
            }

            var ordered = DealPlanner.ComputeOptimalOrder(ScheduleOneNavigatorMod.GetTrackedPosition(player), feasible);
            var stops = new List<Vector3>(ordered.Count);
            foreach (var d in ordered)
                stops.Add(d.DeliveryPosition);

            MelonLogger.Msg($"[Minimap] FullMapView: optimal route set through {stops.Count} deal(s): " +
                string.Join(" -> ", ordered.ConvertAll(d => d.CustomerName)));
            routePlanner.SetRoute(stops);
        }

        // Same destroy/rebuild shape as RefreshDeals, but a single-line row
        // (just the business's name - no product/feasibility sub-rows,
        // businesses don't have those).
        void RefreshBusinessList()
        {
            currentBusinesses.Clear();
            var allBusinesses = Business.Businesses;
            if (allBusinesses != null)
                foreach (Business b in allBusinesses)
                    if (b != null)
                        currentBusinesses.Add(b);

            foreach (var row in businessRows)
                GameObject.Destroy(row.rect.gameObject);
            businessRows.Clear();

            for (int i = 0; i < currentBusinesses.Count; i++)
            {
                Business business = currentBusinesses[i];

                GameObject rowGO = new GameObject("BusinessRow");
                rowGO.transform.SetParent(dealListContainer, false);
                RectTransform rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(1f, 1f);
                rowRT.pivot = new Vector2(0.5f, 1f);
                rowRT.anchoredPosition = new Vector2(0f, -i * BusinessRowHeight);
                rowRT.sizeDelta = new Vector2(0f, BusinessRowHeight - 4f);
                rowGO.AddComponent<Image>().color = BusinessRowColor;

                GameObject nameGO = new GameObject("Name");
                nameGO.transform.SetParent(rowGO.transform, false);
                RectTransform nameRT = nameGO.AddComponent<RectTransform>();
                nameRT.anchorMin = Vector2.zero;
                nameRT.anchorMax = Vector2.one;
                nameRT.offsetMin = new Vector2(8f, 0f);
                nameRT.offsetMax = new Vector2(-8f, 0f);
                Text nameText = nameGO.AddComponent<Text>();
                nameText.font = GetFont();
                nameText.text = business.PropertyName;
                nameText.fontSize = 15;
                nameText.color = DealRowTextColor;
                nameText.alignment = TextAnchor.MiddleLeft;
                nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                nameText.verticalOverflow = VerticalWrapMode.Truncate;

                businessRows.Add((rowRT, business));
            }

            dealListContainer.sizeDelta = new Vector2(0f, currentBusinesses.Count * BusinessRowHeight);
        }

        Business FindClickedBusinessRow(Vector2 screenPoint)
        {
            foreach (var row in businessRows)
                if (row.rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(row.rect, screenPoint))
                    return row.business;
            return null;
        }

        // Same destroy/rebuild shape as RefreshBusinessList, but grouped into
        // two sections (Dealer / Supplier, each alphabetical by FullName)
        // with a non-clickable header row per section - added 2026-09-19 per
        // user request, so the two hierarchies (see IsDealerNpc/
        // IsSupplierNpc) are easy to tell apart at a glance instead of one
        // flat, registry-ordered list. Each row also shows
        // RelationData.Unlocked (row color + name suffix), since not every
        // dealer/supplier is reachable yet depending on story progress.
        void RefreshDealerList()
        {
            currentDealerNpcs.Clear();
            var dealerGroup = new List<NPC>();
            var supplierGroup = new List<NPC>();
            var npcs = NPCManager.NPCRegistry;
            if (npcs != null)
                foreach (NPC npc in npcs)
                {
                    // No activeInHierarchy check here (unlike
                    // RefreshDealerMarkers) - confirmed live 2026-09-19 that
                    // requiring it hid already-unlocked dealers/suppliers
                    // from the LIST entirely just because they weren't
                    // currently spawned nearby at that moment. The list is
                    // "who do you know", independent of whether they're
                    // physically around right now; only the map MARKER
                    // needs a real, currently-active position.
                    if (npc == null)
                        continue;
                    if (IsDealerNpc(npc))
                        dealerGroup.Add(npc);
                    else if (IsSupplierNpc(npc))
                        supplierGroup.Add(npc);
                }
            dealerGroup.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            supplierGroup.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            currentDealerNpcs.AddRange(dealerGroup);
            currentDealerNpcs.AddRange(supplierGroup);

            foreach (var row in dealerRows)
                GameObject.Destroy(row.rect.gameObject);
            dealerRows.Clear();
            foreach (var header in dealerHeaderRows)
                GameObject.Destroy(header.gameObject);
            dealerHeaderRows.Clear();

            float y = 0f;

            void AddHeader(string label)
            {
                GameObject headerGO = new GameObject("DealerHeader");
                headerGO.transform.SetParent(dealListContainer, false);
                RectTransform headerRT = headerGO.AddComponent<RectTransform>();
                headerRT.anchorMin = new Vector2(0f, 1f);
                headerRT.anchorMax = new Vector2(1f, 1f);
                headerRT.pivot = new Vector2(0.5f, 1f);
                headerRT.anchoredPosition = new Vector2(0f, -y);
                headerRT.sizeDelta = new Vector2(0f, SectionHeaderRowHeight);
                headerGO.AddComponent<Image>().color = SectionHeaderRowColor;

                GameObject textGO = new GameObject("Text");
                textGO.transform.SetParent(headerGO.transform, false);
                RectTransform textRT = textGO.AddComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(8f, 0f);
                textRT.offsetMax = new Vector2(-8f, 0f);
                Text text = textGO.AddComponent<Text>();
                text.font = GetFont();
                text.text = label;
                text.fontSize = 13;
                text.fontStyle = FontStyle.Bold;
                text.color = SectionHeaderTextColor;
                text.alignment = TextAnchor.MiddleLeft;

                dealerHeaderRows.Add(headerRT);
                y += SectionHeaderRowHeight;
            }

            void AddRow(NPC npc)
            {
                bool available = npc.RelationData != null && npc.RelationData.Unlocked;

                GameObject rowGO = new GameObject("DealerRow");
                rowGO.transform.SetParent(dealListContainer, false);
                RectTransform rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(1f, 1f);
                rowRT.pivot = new Vector2(0.5f, 1f);
                rowRT.anchoredPosition = new Vector2(0f, -y);
                rowRT.sizeDelta = new Vector2(0f, DealerRowHeight - 4f);
                rowGO.AddComponent<Image>().color = available ? DealerRowAvailableColor : DealerRowUnavailableColor;

                GameObject nameGO = new GameObject("Name");
                nameGO.transform.SetParent(rowGO.transform, false);
                RectTransform nameRT = nameGO.AddComponent<RectTransform>();
                nameRT.anchorMin = Vector2.zero;
                nameRT.anchorMax = Vector2.one;
                nameRT.offsetMin = new Vector2(8f, 0f);
                nameRT.offsetMax = new Vector2(-8f, 0f);
                Text nameText = nameGO.AddComponent<Text>();
                nameText.font = GetFont();
                nameText.text = available ? npc.FullName : $"{npc.FullName} (noch nicht freigeschaltet)";
                nameText.fontSize = 15;
                nameText.color = available ? DealRowTextColor : DealRowSubTextColor;
                nameText.alignment = TextAnchor.MiddleLeft;
                nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                nameText.verticalOverflow = VerticalWrapMode.Truncate;

                dealerRows.Add((rowRT, npc));
                y += DealerRowHeight;
            }

            if (dealerGroup.Count > 0)
            {
                AddHeader("Dealer");
                foreach (NPC npc in dealerGroup)
                    AddRow(npc);
            }
            if (supplierGroup.Count > 0)
            {
                AddHeader("Supplier");
                foreach (NPC npc in supplierGroup)
                    AddRow(npc);
            }

            dealListContainer.sizeDelta = new Vector2(0f, y);
        }

        NPC FindClickedDealerRow(Vector2 screenPoint)
        {
            foreach (var row in dealerRows)
                if (row.rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(row.rect, screenPoint))
                    return row.npc;
            return null;
        }

        // Same shape as RefreshDealerList - grouped into "Gekauft"/"Nicht
        // gekauft" sections (alphabetical within each) with a header row per
        // section. Unowned rows are still listed (grayed, with the asking
        // price shown) but excluded from click-routing in
        // FindClickedPropertyRow, matching how their marker is omitted
        // entirely in RefreshPropertyMarkers - the user asked for unowned
        // entries to be inert, not just visually dimmed.
        void RefreshPropertyList()
        {
            currentProperties.Clear();
            var ownedGroup = new List<Property>();
            var unownedGroup = new List<Property>();
            var allProperties = Property.Properties;
            if (allProperties != null)
                foreach (Property p in allProperties)
                {
                    if (p == null)
                        continue;
                    (p.IsOwned ? ownedGroup : unownedGroup).Add(p);
                }
            ownedGroup.Sort((a, b) => string.CompareOrdinal(a.PropertyName, b.PropertyName));
            unownedGroup.Sort((a, b) => string.CompareOrdinal(a.PropertyName, b.PropertyName));
            currentProperties.AddRange(ownedGroup);
            currentProperties.AddRange(unownedGroup);

            foreach (var row in propertyRows)
                GameObject.Destroy(row.rect.gameObject);
            propertyRows.Clear();
            foreach (var header in propertyHeaderRows)
                GameObject.Destroy(header.gameObject);
            propertyHeaderRows.Clear();

            float y = 0f;

            void AddHeader(string label)
            {
                GameObject headerGO = new GameObject("PropertyHeader");
                headerGO.transform.SetParent(dealListContainer, false);
                RectTransform headerRT = headerGO.AddComponent<RectTransform>();
                headerRT.anchorMin = new Vector2(0f, 1f);
                headerRT.anchorMax = new Vector2(1f, 1f);
                headerRT.pivot = new Vector2(0.5f, 1f);
                headerRT.anchoredPosition = new Vector2(0f, -y);
                headerRT.sizeDelta = new Vector2(0f, SectionHeaderRowHeight);
                headerGO.AddComponent<Image>().color = SectionHeaderRowColor;

                GameObject textGO = new GameObject("Text");
                textGO.transform.SetParent(headerGO.transform, false);
                RectTransform textRT = textGO.AddComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(8f, 0f);
                textRT.offsetMax = new Vector2(-8f, 0f);
                Text text = textGO.AddComponent<Text>();
                text.font = GetFont();
                text.text = label;
                text.fontSize = 13;
                text.fontStyle = FontStyle.Bold;
                text.color = SectionHeaderTextColor;
                text.alignment = TextAnchor.MiddleLeft;

                propertyHeaderRows.Add(headerRT);
                y += SectionHeaderRowHeight;
            }

            void AddRow(Property p)
            {
                GameObject rowGO = new GameObject("PropertyRow");
                rowGO.transform.SetParent(dealListContainer, false);
                RectTransform rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(1f, 1f);
                rowRT.pivot = new Vector2(0.5f, 1f);
                rowRT.anchoredPosition = new Vector2(0f, -y);
                rowRT.sizeDelta = new Vector2(0f, PropertyRowHeight - 4f);
                rowGO.AddComponent<Image>().color = p.IsOwned ? PropertyRowOwnedColor : PropertyRowUnownedColor;

                GameObject nameGO = new GameObject("Name");
                nameGO.transform.SetParent(rowGO.transform, false);
                RectTransform nameRT = nameGO.AddComponent<RectTransform>();
                nameRT.anchorMin = Vector2.zero;
                nameRT.anchorMax = Vector2.one;
                nameRT.offsetMin = new Vector2(8f, 0f);
                nameRT.offsetMax = new Vector2(-8f, 0f);
                Text nameText = nameGO.AddComponent<Text>();
                nameText.font = GetFont();
                nameText.text = p.IsOwned ? p.PropertyName : $"{p.PropertyName} (${p.Price:F0})";
                nameText.fontSize = 15;
                nameText.color = p.IsOwned ? DealRowTextColor : DealRowSubTextColor;
                nameText.alignment = TextAnchor.MiddleLeft;
                nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                nameText.verticalOverflow = VerticalWrapMode.Truncate;

                propertyRows.Add((rowRT, p));
                y += PropertyRowHeight;
            }

            if (ownedGroup.Count > 0)
            {
                AddHeader("Gekauft");
                foreach (Property p in ownedGroup)
                    AddRow(p);
            }
            if (unownedGroup.Count > 0)
            {
                AddHeader("Nicht gekauft");
                foreach (Property p in unownedGroup)
                    AddRow(p);
            }

            dealListContainer.sizeDelta = new Vector2(0f, y);
        }

        Property FindClickedPropertyRow(Vector2 screenPoint)
        {
            foreach (var row in propertyRows)
                if (row.property.IsOwned && row.rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(row.rect, screenPoint))
                    return row.property;
            return null;
        }

        // Tab strip: which button - if any - the given screen point hits.
        // Checked before anything else in Tick()'s click handling so a tab
        // click can never also register as a row/marker/map click
        // underneath it.
        int FindClickedTab(Vector2 screenPoint)
        {
            for (int i = 0; i < tabButtonRects.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(tabButtonRects[i], screenPoint))
                    return i;
            return -1;
        }

        void SetViewMode(MapViewMode mode)
        {
            if (viewMode == mode)
                return;
            viewMode = mode;
            // Force an immediate refresh of the tab being switched into,
            // instead of waiting out its refresh interval.
            if (mode == MapViewMode.Deals)
                dealRefreshTimer = 0f;
            else if (mode == MapViewMode.Shops)
                shopRefreshTimer = 0f;
            else if (mode == MapViewMode.Businesses)
                businessRefreshTimer = 0f;
            else if (mode == MapViewMode.Dealers)
                dealerRefreshTimer = 0f;
            else if (mode == MapViewMode.Properties)
                propertyRefreshTimer = 0f;
            UpdatePanelForViewMode();
        }

        // Panel title/button visibility/tab highlighting - anything that
        // depends on which tab is currently selected, gathered in one place
        // so SetViewMode and EnsureCreated (initial state) can both call it.
        void UpdatePanelForViewMode()
        {
            dealTitleText.text = viewMode switch
            {
                MapViewMode.Deals => "Aktuelle Deals",
                MapViewMode.Shops => "Shops",
                MapViewMode.Businesses => "Businesses",
                MapViewMode.Dealers => "Dealer & Supplier",
                MapViewMode.Properties => "Eigentum",
                _ => "Bald verfügbar",
            };

            bool showRouteButton = viewMode == MapViewMode.Deals;
            routeButtonGO.SetActive(showRouteButton);
            dealListViewportRT.offsetMin = new Vector2(0f, showRouteButton ? DealButtonHeight + 6f : 4f);

            for (int i = 0; i < tabButtonImages.Length; i++)
                tabButtonImages[i].color = (int)viewMode == i ? TabActiveColor : TabInactiveColor;

            // Deal/shop/business rows all share the same dealListContainer -
            // hide whichever tab's rows aren't active so they don't bleed
            // through the other tabs' lists (they'd otherwise stay active
            // until their own refresh next runs, which is now gated to only
            // run while that tab is selected).
            foreach (var row in dealRows)
                row.rect.gameObject.SetActive(viewMode == MapViewMode.Deals);
            foreach (var header in dealHeaderRows)
                header.gameObject.SetActive(viewMode == MapViewMode.Deals);
            foreach (var row in shopRows)
                row.rect.gameObject.SetActive(viewMode == MapViewMode.Shops);
            foreach (var row in businessRows)
                row.rect.gameObject.SetActive(viewMode == MapViewMode.Businesses);
            foreach (var row in dealerRows)
                row.rect.gameObject.SetActive(viewMode == MapViewMode.Dealers);
            foreach (var header in dealerHeaderRows)
                header.gameObject.SetActive(viewMode == MapViewMode.Dealers);
            foreach (var row in propertyRows)
                row.rect.gameObject.SetActive(viewMode == MapViewMode.Properties);
            foreach (var header in propertyHeaderRows)
                header.gameObject.SetActive(viewMode == MapViewMode.Properties);
        }

        RectTransform GetOrCreateRouteDot(int index)
        {
            if (index < routeDotPool.Count)
                return routeDotPool[index];

            GameObject dotGO = new GameObject("RouteDot");
            dotGO.transform.SetParent(viewport, false);
            RectTransform rt = dotGO.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RouteDotSize, RouteDotSize);
            dotGO.AddComponent<Image>().sprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)RouteDotSize * 2, RouteDotColor);
            routeDotPool.Add(rt);
            return rt;
        }

        void SetVisible(bool value)
        {
            visible = value;
            if (visible)
                EnsureCreated();
            if (rootGO != null)
                rootGO.SetActive(visible);

            if (visible)
            {
                zoomInitialized = false; // recompute the fit-to-view zoom fresh on every open
                panOffset = Vector2.zero;
                dragging = false;

                // The game normally locks/hides the cursor to drive camera look
                // from mouse movement - while our map is open we need a normal,
                // free-moving visible pointer to aim clicks instead, or the
                // mouse just spins the camera and Input.mousePosition stays
                // pinned to screen-center (CursorLockMode.Locked) instead of
                // tracking real aim position.
                savedLockState = Cursor.lockState;
                savedCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // A click on our map must not also register as an attack in
                // the game world underneath (see Tick() and the
                // Phone.ActiveApp comment below - four earlier fixes here
                // didn't stop it, briefly worked around by moving our own
                // clicks to the middle mouse button instead of solving it),
                // and the player shouldn't keep walking/looking around while
                // the tablet covers the screen.
                // PlayerMovement.CanMove, Phone.SetIsOpen (reusing the same
                // gameplay-input gate the game's own phone app relies on) and
                // the PlayerInventory switches below are the targeted gates
                // for this. A Time.timeScale=0 global pause used to sit here
                // too, as a blunt fallback from an earlier session where
                // these three alone weren't enough (attacks/movement still
                // went through in testing) - removed 2026-09-19 per user
                // request, since a global pause is bad for multiplayer (halts
                // Time.deltaTime-driven local simulation - movement
                // interpolation, animations, physics - for the rest of the
                // session while the tablet is open, even though FishNet's
                // network tick itself likely keeps running on unscaled time;
                // the client visibly "catches up"/snaps once closed). If
                // attacks/movement turn out to leak through again without it,
                // that regression needs to be re-diagnosed rather than just
                // reinstating the global pause.
                if (PlayerMovement.InstanceExists)
                    PlayerMovement.Instance.CanMove = false;
                if (Phone.InstanceExists)
                    Phone.Instance.SetIsOpen(true);
                // Phone.IsOpen (above) is probably not what the game's own
                // combat code actually checks - Phone.ActiveApp (a static
                // GameObject, confirmed settable via decompile) backs
                // Phone.IsAnyAppOpen, which reads as a much likelier
                // candidate for that gate, and we'd simply never set it
                // before now. Test (2026-09-19): set it to our own root so
                // the game considers "an app" open the same way it would for
                // any of its native ones.
                Phone.ActiveApp = rootGO;
                // HotbarEnabled/SetEquippingEnabled(false) gate the
                // mouse-wheel-changes-hotbar issue and switching equipment
                // mid-map, but NOT using whatever is already equipped - a map
                // click could still swing/fire the currently-held item. Fix
                // (2026-09-19, replacing the removed Time.timeScale=0
                // workaround): unequip entirely for as long as the map is
                // open, same idea as the user's "give the player a
                // functionless dummy item" suggestion, but simpler - nothing
                // equipped means nothing to attack with, no fake item needed.
                // equippedSlot is saved so the original item comes back on
                // close; Equip(null) is unverified against the native
                // implementation (no decompiled method body available) -
                // needs a live check that it doesn't misbehave.
                if (PlayerInventory.InstanceExists)
                {
                    PlayerInventory.Instance.HotbarEnabled = false;
                    PlayerInventory.Instance.SetEquippingEnabled(false);
                    savedEquippedSlot = PlayerInventory.Instance.equippedSlot;
                    PlayerInventory.Instance.Equip(null);
                    // Diagnostics (2026-09-19) - PunchController's own state
                    // stayed false/false/false through clicks last test, so
                    // unarmed punching isn't actually what's happening -
                    // check whether Equip(null) really unequipped, since the
                    // attack could be the (still-equipped) item's own.
                    MelonLogger.Msg($"[Minimap] FullMapView: equip diag on open - " +
                        $"savedEquippedSlot={(savedEquippedSlot != null ? "non-null" : "null")}, " +
                        $"isAnythingEquipped after Equip(null)={PlayerInventory.Instance.isAnythingEquipped}, " +
                        $"EquippedItem after Equip(null)={(PlayerInventory.Instance.EquippedItem != null ? PlayerInventory.Instance.EquippedItem.ToString() : "null")}");
                }

                // Live test (2026-09-19) showed the player could still punch
                // with all of the above in place - unarmed melee turns out
                // to be a wholly separate system (PunchController), not
                // gated by anything on PlayerInventory at all. Equip(null)
                // above may have made this worse rather than better if
                // PunchController auto-enables itself on empty hands (its
                // itemEquippedLastFrame field suggests it reacts to equip
                // state) - explicitly disabling it here, plus a per-frame
                // reassertion in Tick() in case its own Update() re-enables
                // itself before ours runs in the same frame (Unity doesn't
                // guarantee MelonLoader's OnUpdate runs after every other
                // MonoBehaviour's Update in a given frame).
                Player localPlayer = Player.Local;
                punchController = localPlayer != null
                    ? localPlayer.GetComponentInChildren<Il2CppScheduleOne.Combat.PunchController>()
                    : null;
                if (punchController != null)
                    punchController.SetPunchingEnabled(false);
                // Diagnostics (2026-09-19) - two targeted fixes (Equip(null),
                // SetPunchingEnabled(false) + per-frame reassertion) failed
                // live, so log hard facts instead of guessing a third time:
                // was the controller even found, and did our call actually
                // take effect this frame?
                MelonLogger.Msg($"[Minimap] FullMapView: punch-block diag on open - " +
                    $"localPlayer={(localPlayer != null)}, punchController found={(punchController != null)}, " +
                    $"PunchingEnabled after SetPunchingEnabled(false)={(punchController != null ? punchController.PunchingEnabled.ToString() : "n/a")}");
            }
            else
            {
                Cursor.lockState = savedLockState;
                Cursor.visible = savedCursorVisible;

                if (punchController != null)
                {
                    punchController.SetPunchingEnabled(true);
                    punchController = null;
                }

                if (PlayerMovement.InstanceExists)
                    PlayerMovement.Instance.CanMove = true;
                if (Phone.InstanceExists)
                    Phone.Instance.SetIsOpen(false);
                if (Phone.ActiveApp == rootGO)
                    Phone.ActiveApp = null;
                if (PlayerInventory.InstanceExists)
                {
                    PlayerInventory.Instance.Equip(savedEquippedSlot);
                    PlayerInventory.Instance.HotbarEnabled = true;
                    PlayerInventory.Instance.SetEquippingEnabled(true);
                }
            }
        }

        void EnsureCreated()
        {
            if (rootGO != null)
                return;

            GameObject canvasGO = new GameObject("FullMapViewCanvas");
            GameObject.DontDestroyOnLoad(canvasGO);
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasGO.AddComponent<CanvasScaler>();
            // Kept for general UI correctness (a Canvas without a
            // GraphicRaycaster is invisible to Unity's EventSystem), though
            // it turned out NOT to be what was causing left-click-while-
            // tablet-open to also register as a world attack - that guess
            // (2026-09-19) tested clean but didn't fix the bug. The actual
            // fix was routing our own click handling through the middle
            // mouse button instead (see Tick()), sidestepping whatever the
            // real cause is rather than continuing to guess against
            // decompiled signatures with no visible method bodies.
            canvasGO.AddComponent<GraphicRaycaster>();
            rootGO = canvasGO;

            GameObject dimGO = new GameObject("Dim");
            dimGO.transform.SetParent(canvasGO.transform, false);
            RectTransform dimRT = dimGO.AddComponent<RectTransform>();
            dimRT.anchorMin = Vector2.zero;
            dimRT.anchorMax = Vector2.one;
            dimRT.offsetMin = Vector2.zero;
            dimRT.offsetMax = Vector2.zero;
            dimGO.AddComponent<Image>().color = DimColor;

            // "Tablet" device frame: dark bezel, header bar with a title, a
            // deal-list panel on the left, a square screen area (masked,
            // holds the actual map) on the right, footer hint along the
            // bottom.
            float screenSize = Mathf.Min(Screen.width, Screen.height) * DeviceFraction;
            screenSizePx = screenSize;
            float deviceWidth = screenSize + DealPanelWidth + BezelPadding * 3f;
            float deviceHeight = screenSize + BezelPadding * 2f + HeaderHeight + FooterHeight;

            GameObject deviceGO = new GameObject("TabletDevice");
            deviceGO.transform.SetParent(canvasGO.transform, false);
            RectTransform deviceRT = deviceGO.AddComponent<RectTransform>();
            deviceRT.anchorMin = deviceRT.anchorMax = new Vector2(0.5f, 0.5f);
            deviceRT.pivot = new Vector2(0.5f, 0.5f);
            deviceRT.sizeDelta = new Vector2(deviceWidth, deviceHeight);
            deviceGO.AddComponent<Image>().color = BezelColor;

            GameObject headerGO = new GameObject("Header");
            headerGO.transform.SetParent(deviceGO.transform, false);
            RectTransform headerRT = headerGO.AddComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.anchoredPosition = new Vector2(0f, -BezelPadding);
            headerRT.sizeDelta = new Vector2(-BezelPadding * 2f, HeaderHeight);
            headerGO.AddComponent<Image>().color = HeaderColor;

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(headerGO.transform, false);
            RectTransform titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = Vector2.zero;
            titleRT.anchorMax = Vector2.one;
            titleRT.offsetMin = new Vector2(16f, 0f);
            titleRT.offsetMax = new Vector2(-16f, 0f);
            Text titleText = titleGO.AddComponent<Text>();
            titleText.font = GetFont();
            titleText.text = "Karte";
            titleText.fontSize = 24;
            titleText.color = TitleColor;
            titleText.alignment = TextAnchor.MiddleLeft;

            GameObject footerGO = new GameObject("Footer");
            footerGO.transform.SetParent(deviceGO.transform, false);
            RectTransform footerRT = footerGO.AddComponent<RectTransform>();
            footerRT.anchorMin = new Vector2(0f, 0f);
            footerRT.anchorMax = new Vector2(1f, 0f);
            footerRT.pivot = new Vector2(0.5f, 0f);
            footerRT.anchoredPosition = new Vector2(0f, BezelPadding);
            footerRT.sizeDelta = new Vector2(-BezelPadding * 2f, FooterHeight);
            Text hint = footerGO.AddComponent<Text>();
            hint.font = GetFont();
            hint.text = "Linksklick: Ziel setzen    |    Rechtsklick ziehen: Karte verschieben    |    ESC / #: Schließen";
            hint.fontSize = 15;
            hint.color = HintColor;
            hint.alignment = TextAnchor.MiddleCenter;
            hintText = hint;

            float bodyY = (FooterHeight - HeaderHeight) / 2f;

            GameObject viewportGO = new GameObject("Screen");
            viewportGO.transform.SetParent(deviceGO.transform, false);
            viewport = viewportGO.AddComponent<RectTransform>();
            viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.sizeDelta = new Vector2(screenSize, screenSize);
            viewport.anchoredPosition = new Vector2(deviceWidth / 2f - BezelPadding - screenSize / 2f, bodyY);
            viewportGO.AddComponent<Image>().color = ScreenBgColor;
            viewportGO.AddComponent<RectMask2D>();

            // Map content: pivot must be centered (0.5,0.5), NOT top-left as
            // an earlier attempt assumed. The anchoredPosition = -playerMapPos
            // * zoom formula places local point P=playerMapPos at the
            // viewport's center *regardless* of pivot - pivot only decides
            // which direction the image extends from that centered point. A
            // top-left pivot made the image extend only right/down from the
            // player, so only whatever was to the player's south-east ever
            // appeared in the (correctly centered) viewport - matching a
            // screenshot from testing exactly. Centered pivot makes the image
            // extend evenly in all directions around the player, as intended.
            // Click accuracy is unaffected either way (confirmed in testing) -
            // clickedMapPos and playerMapPos are read from the same transform
            // via the same pivot, so they stay internally consistent.
            GameObject mapGO = new GameObject("MapContainer");
            mapGO.transform.SetParent(viewportGO.transform, false);
            mapContainer = mapGO.AddComponent<RectTransform>();
            mapContainer.anchorMin = new Vector2(0.5f, 0.5f);
            mapContainer.anchorMax = new Vector2(0.5f, 0.5f);
            mapContainer.pivot = new Vector2(0.5f, 0.5f);
            mapImage = mapGO.AddComponent<Image>();
            mapImage.raycastTarget = false; // clicks are polled directly via Input, not routed through the event system

            customerSprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)CustomerDotSize * 2, CustomerDotColor);
            shopSprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)ShopDotSize * 2, ShopDotColor);
            businessSprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)BusinessDotSize * 2, BusinessDotColor);
            dealerSprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)DealerDotSize * 2, DealerDotColor);
            dealerSpriteLocked = ScheduleOneNavigatorMod.CreateCircleSprite((int)DealerDotSize * 2, DealerDotColorLocked);
            propertySprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)PropertyDotSize * 2, PropertyDotColor);
            otherPlayerSprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)OtherPlayerDotSize * 2, OtherPlayerDotColor);

            GameObject dotGO = new GameObject("PlayerDot");
            dotGO.transform.SetParent(viewportGO.transform, false);
            playerDot = dotGO.AddComponent<RectTransform>();
            playerDot.anchorMin = playerDot.anchorMax = new Vector2(0.5f, 0.5f);
            playerDot.pivot = new Vector2(0.5f, 0.5f);
            playerDot.sizeDelta = new Vector2(PlayerDotSize, PlayerDotSize);
            playerDot.anchoredPosition = Vector2.zero;
            dotGO.AddComponent<Image>().sprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)PlayerDotSize * 2, PlayerDotColor);

            GameObject destGO = new GameObject("DestinationDot");
            destGO.transform.SetParent(viewportGO.transform, false);
            destinationDot = destGO.AddComponent<RectTransform>();
            destinationDot.anchorMin = destinationDot.anchorMax = new Vector2(0.5f, 0.5f);
            destinationDot.pivot = new Vector2(0.5f, 0.5f);
            destinationDot.sizeDelta = new Vector2(DestinationDotSize, DestinationDotSize);
            destGO.AddComponent<Image>().sprite = ScheduleOneNavigatorMod.CreateCircleSprite((int)DestinationDotSize * 2, DestinationDotColor);
            destGO.SetActive(false);

            // Deal panel: left side of the device, same height as the map
            // screen. Title, a masked/scrollless row list (rebuilt by
            // RefreshDeals), and the "optimal route" button pinned to the
            // bottom.
            GameObject dealPanelGO = new GameObject("DealPanel");
            dealPanelGO.transform.SetParent(deviceGO.transform, false);
            RectTransform dealPanelRT = dealPanelGO.AddComponent<RectTransform>();
            dealPanelRT.anchorMin = dealPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
            dealPanelRT.pivot = new Vector2(0.5f, 0.5f);
            dealPanelRT.sizeDelta = new Vector2(DealPanelWidth, screenSize);
            dealPanelRT.anchoredPosition = new Vector2(-deviceWidth / 2f + BezelPadding + DealPanelWidth / 2f, bodyY);
            dealPanelGO.AddComponent<Image>().color = DealPanelColor;

            // Tab row: 4 equal-width buttons above the title, switching which
            // list (Deals/Shops/reserved) the panel below shows - see
            // FindClickedTab/SetViewMode.
            GameObject tabRowGO = new GameObject("TabRow");
            tabRowGO.transform.SetParent(dealPanelGO.transform, false);
            RectTransform tabRowRT = tabRowGO.AddComponent<RectTransform>();
            tabRowRT.anchorMin = new Vector2(0f, 1f);
            tabRowRT.anchorMax = new Vector2(1f, 1f);
            tabRowRT.pivot = new Vector2(0.5f, 1f);
            tabRowRT.anchoredPosition = Vector2.zero;
            tabRowRT.sizeDelta = new Vector2(0f, TabRowHeight);

            string[] tabLabels = { "Deals", "Shops", "Businesses", "Dealers", "Eigentum" };
            float tabWidth = DealPanelWidth / tabButtonRects.Length;
            for (int i = 0; i < tabButtonRects.Length; i++)
            {
                GameObject tabGO = new GameObject($"Tab{i}");
                tabGO.transform.SetParent(tabRowGO.transform, false);
                RectTransform tabRT = tabGO.AddComponent<RectTransform>();
                tabRT.anchorMin = new Vector2(0f, 0f);
                tabRT.anchorMax = new Vector2(0f, 1f);
                tabRT.pivot = new Vector2(0f, 0.5f);
                tabRT.anchoredPosition = new Vector2(i * tabWidth, 0f);
                tabRT.sizeDelta = new Vector2(tabWidth - 2f, 0f);
                Image tabImage = tabGO.AddComponent<Image>();
                tabButtonRects[i] = tabRT;
                tabButtonImages[i] = tabImage;

                GameObject tabTextGO = new GameObject("Text");
                tabTextGO.transform.SetParent(tabGO.transform, false);
                RectTransform tabTextRT = tabTextGO.AddComponent<RectTransform>();
                tabTextRT.anchorMin = Vector2.zero;
                tabTextRT.anchorMax = Vector2.one;
                tabTextRT.offsetMin = Vector2.zero;
                tabTextRT.offsetMax = Vector2.zero;
                Text tabText = tabTextGO.AddComponent<Text>();
                tabText.font = GetFont();
                tabText.text = tabLabels[i];
                tabText.fontSize = 13;
                tabText.color = TitleColor;
                tabText.alignment = TextAnchor.MiddleCenter;
                tabButtonTexts[i] = tabText;
            }

            GameObject dealTitleGO = new GameObject("Title");
            dealTitleGO.transform.SetParent(dealPanelGO.transform, false);
            dealTitleRT = dealTitleGO.AddComponent<RectTransform>();
            dealTitleRT.anchorMin = new Vector2(0f, 1f);
            dealTitleRT.anchorMax = new Vector2(1f, 1f);
            dealTitleRT.pivot = new Vector2(0.5f, 1f);
            dealTitleRT.anchoredPosition = new Vector2(0f, -TabRowHeight);
            dealTitleRT.sizeDelta = new Vector2(-16f, DealListTitleHeight);
            dealTitleText = dealTitleGO.AddComponent<Text>();
            dealTitleText.font = GetFont();
            dealTitleText.text = "Aktuelle Deals";
            dealTitleText.fontSize = 16;
            dealTitleText.color = TitleColor;
            dealTitleText.alignment = TextAnchor.MiddleLeft;
            dealTitleText.fontStyle = FontStyle.Bold;

            GameObject dealListViewportGO = new GameObject("ListViewport");
            dealListViewportGO.transform.SetParent(dealPanelGO.transform, false);
            dealListViewportRT = dealListViewportGO.AddComponent<RectTransform>();
            dealListViewportRT.anchorMin = new Vector2(0f, 0f);
            dealListViewportRT.anchorMax = new Vector2(1f, 1f);
            dealListViewportRT.offsetMin = new Vector2(0f, DealButtonHeight + 6f);
            dealListViewportRT.offsetMax = new Vector2(0f, -(TabRowHeight + DealListTitleHeight));
            dealListViewportGO.AddComponent<RectMask2D>();

            GameObject dealListGO = new GameObject("Rows");
            dealListGO.transform.SetParent(dealListViewportGO.transform, false);
            dealListContainer = dealListGO.AddComponent<RectTransform>();
            dealListContainer.anchorMin = new Vector2(0f, 1f);
            dealListContainer.anchorMax = new Vector2(1f, 1f);
            dealListContainer.pivot = new Vector2(0.5f, 1f);
            dealListContainer.anchoredPosition = Vector2.zero;
            dealListContainer.sizeDelta = new Vector2(0f, 0f);

            routeButtonGO = new GameObject("OptimalRouteButton");
            routeButtonGO.transform.SetParent(dealPanelGO.transform, false);
            routeButtonRect = routeButtonGO.AddComponent<RectTransform>();
            routeButtonRect.anchorMin = new Vector2(0f, 0f);
            routeButtonRect.anchorMax = new Vector2(1f, 0f);
            routeButtonRect.pivot = new Vector2(0.5f, 0f);
            routeButtonRect.anchoredPosition = new Vector2(0f, 4f);
            routeButtonRect.sizeDelta = new Vector2(-12f, DealButtonHeight);
            routeButtonImage = routeButtonGO.AddComponent<Image>();
            routeButtonImage.color = RouteButtonDisabledColor;

            GameObject routeButtonTextGO = new GameObject("Text");
            routeButtonTextGO.transform.SetParent(routeButtonGO.transform, false);
            RectTransform routeButtonTextRT = routeButtonTextGO.AddComponent<RectTransform>();
            routeButtonTextRT.anchorMin = Vector2.zero;
            routeButtonTextRT.anchorMax = Vector2.one;
            routeButtonTextRT.offsetMin = Vector2.zero;
            routeButtonTextRT.offsetMax = Vector2.zero;
            routeButtonText = routeButtonTextGO.AddComponent<Text>();
            routeButtonText.font = GetFont();
            routeButtonText.text = "Optimale Route";
            routeButtonText.fontSize = 15;
            routeButtonText.color = TitleColor;
            routeButtonText.alignment = TextAnchor.MiddleCenter;

            UpdatePanelForViewMode();

            MelonLogger.Msg("[Minimap] FullMapView created.");
        }

        static Font GetFont()
        {
            if (cachedFont != null)
                return cachedFont;

            // Unity 2018.3+ renamed the built-in legacy font resource from
            // "Arial.ttf" to "LegacyRuntime.ttf" - try the current name first,
            // fall back to the old one for safety.
            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cachedFont == null)
                cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return cachedFont;
        }
    }
}
