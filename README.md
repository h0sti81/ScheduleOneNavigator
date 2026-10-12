# ScheduleOne Navigator

A full-screen map and navigation overlay for **Schedule I**, built on top of [MelonLoader](https://github.com/LavaGang/MelonLoader). It opens as a real app on your in-game phone and gives you click-to-route pathfinding with tabs for your active deals, shops, businesses, properties, and dealers/suppliers — plus a small always-on minimap in the corner of the HUD.

**[Download the latest release](https://github.com/h0sti81/ScheduleOneNavigator/releases/latest)**

## Features

- **Minimap** (top-right HUD): live top-down view centered on the player (follows you correctly while driving too), with other multiplayer players shown as green dots and recruitable customers highlighted with a red aura.
- **Full map overlay**: press `M` or open the phone's **Map** app - pan and click anywhere to set a route, shown as a dotted line and followed on the minimap.
- **Vehicle-aware routing**: on foot, routes go anywhere you can walk (lightly preferring roads and sidewalks); in a vehicle they take the shortest way onto the road network and stay on it until close to the destination.
- **Deals tab**: your currently active delivery contracts (plus deals already scheduled but not yet awaiting delivery), with an "optimal route" button that plans a delivery tour for the stock you actually carry: deals that expire soonest first, then always the nearest customer your remaining stock still covers. All stops are shown numbered on the map, and the tour re-plans itself as deals are handed over, restocked, added or expire. Recruitable customers are shown here too (red aura) and can be clicked to route to them.
- **Shops tab**: real in-world retail locations (gas stations, hardware stores, clothing, pawn shop, casino, barbershop, tattoo parlor, auto dealerships, ...), click a marker or list entry to route to it.
- **Businesses tab**: your money-laundering fronts (laundromat, car wash, ...); ones still for sale are listed with their price.
- **Dealers & Suppliers tab**: every dealer and supplier NPC, grouped and sorted, with a clear visual indicator for which ones you've already unlocked.
- **Properties tab**: every property in the game, grouped into owned / locked (e.g. the Sewer Office before you have its key) / for sale - owned ones are fully navigable, the others are grayed out.
- Other connected players are shown as a green dot (with name label) on both the minimap and the full map.

## Requirements

- **Schedule I** (Steam)
- **MelonLoader**, version 0.7.3 or newer (Open Beta / Il2Cpp build) — [official repository](https://github.com/LavaGang/MelonLoader)

MelonLoader works the same way whether you're on Windows or on Linux/Steam Deck via Proton — install it first if you haven't already, following the instructions on the MelonLoader repository.

## Installation

1. Make sure MelonLoader is installed for Schedule I, and start the game at least once with it so the `Mods` folder gets created.
2. Download `ScheduleOneNavigator.dll` from the [latest release](https://github.com/h0sti81/ScheduleOneNavigator/releases/latest).
3. Open your Schedule I install folder:
   - **Steam (Windows/Linux/Steam Deck):** right-click **Schedule I** in your Steam library → **Manage** → **Browse local files**.
4. Copy `ScheduleOneNavigator.dll` into the `Mods` subfolder (create it if it's missing — it usually already exists after step 1).
5. Launch the game normally.

## Usage

- Press `M`, or open your phone and tap the **Map** app, to open the full map overlay.
- Click a tab (Deals / Shops / Biz / Dealers / Properties) to switch views.
- Click anywhere on the map, or on a marker/list row, to set a route there. Press `M` or `TAB` to close it back to the game, or `Esc` / right-click to go back to the phone's home screen.
- Right-click-drag to pan the map.

## Troubleshooting / bug reports

**Crashes or errors:** download `CollectCrashInfo.bat` (from the [releases page](https://github.com/h0sti81/ScheduleOneNavigator/releases) or [`tools/`](tools/CollectCrashInfo.bat)) and double-click it after the problem happened. It finds your game folder, collects the MelonLoader logs, Unity's `Player.log` and crash reports, Windows' crash events and a list of installed mods, and puts everything into one zip on your desktop - attach that zip to your report. Nothing is uploaded, and your Windows user name is removed from the logs. (Windows only; on Linux/Proton please attach `MelonLoader/Latest.log` by hand.)

The mod keeps the MelonLoader log quiet by default. To get detailed pathfinding and UI diagnostics for a bug report, start the game once with the mod installed, then set this in `UserData/MelonPreferences.cfg` and restart:

```
[ScheduleOneNavigator]
VerboseLogging = true
```

The output ends up in `MelonLoader/Latest.log`.

## Uninstalling

Delete `ScheduleOneNavigator.dll` from the `Mods` folder.

## Compatibility

Doesn't modify save data or other mods' files. The phone's **Map** app opens this mod's full map instead of the small built-in one. Everything else is a pure overlay/UI addition and shouldn't conflict with anything else. Works on both Windows and Linux/Steam Deck via Proton, with one known Proton-specific quirk below.

## Known Issues

- **Windowed mode + Linux/Proton: map clicks can land offset from where you actually clicked.** Happens when the game is set to windowed and you switch to fullscreen (e.g. via a window-manager shortcut like Super+F) while it's still loading - a Wine/Proton pointer-position desync (the OS/compositor↔Wine coordinate transform doesn't get recomputed until the window goes through a real fullscreen transition), not a bug in this mod's own click math. **Workaround:** toggle fullscreen once (into fullscreen and back to windowed) after the game has finished loading, before opening the map - the fix is then stable for the rest of the session, even switching between windowed and fullscreen afterward. Not observed on native Windows or when launching directly in fullscreen/borderless.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for all changes, or the [releases page](https://github.com/h0sti81/ScheduleOneNavigator/releases) for downloadable builds.

## Credits

Created by h0sti.
