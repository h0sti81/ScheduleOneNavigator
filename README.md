# ScheduleOne Navigator

A full-screen map and navigation overlay for **Schedule I**, built on top of [MelonLoader](https://github.com/LavaGang/MelonLoader). It opens as a real app on your in-game phone and gives you click-to-route pathfinding with tabs for your active deals, shops, businesses, properties, and dealers/suppliers — plus a small always-on minimap in the corner of the HUD.

**[Download the latest release](https://github.com/h0sti81/ScheduleOneNavigator/releases/latest)**

## Features

- **Minimap** (top-right HUD): live top-down view centered on the player (follows you correctly while driving too), with other multiplayer players shown as green dots and recruitable customers highlighted with a red aura.
- **Full map overlay**: opens from your phone's home screen (look for the "N" icon) - pan and click anywhere to set a route, shown as a dotted line and followed on the minimap.
- **Deals tab**: your currently active delivery contracts (plus deals already scheduled but not yet awaiting delivery), with an "optimal route" button that orders all fulfillable deals into a single efficient route based on what's in your inventory. Recruitable customers are shown here too (red aura) and can be clicked to route to them.
- **Shops tab**: real in-world retail locations (gas stations, hardware stores, clothing, pawn shop, casino, barbershop, tattoo parlor, auto dealerships, ...), click a marker or list entry to route to it.
- **Businesses tab**: your production properties (warehouse, sweatshop, motel, etc.).
- **Dealers & Suppliers tab**: every dealer and supplier NPC, grouped and sorted, with a clear visual indicator for which ones you've already unlocked.
- **Properties tab ("Eigentum")**: every property in the game, grouped into owned/not-yet-owned - owned ones are fully navigable, unowned ones show their price and are grayed out.
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

- Open your phone as usual and tap the "N" app icon to open the full map overlay.
- Click a tab (Deals / Shops / Businesses / Dealers / Eigentum) to switch views.
- Click anywhere on the map, or on a marker/list row, to set a route there. Press `Esc` or close the phone to close it.
- Right-click-drag to pan the map.

## Uninstalling

Delete `ScheduleOneNavigator.dll` from the `Mods` folder.

## Compatibility

Doesn't modify save data or other mods' files. One exception: it repurposes the phone's native Map app (icon relabeled to "N") to open this mod's full map instead - the original small in-phone map screen is no longer accessible while this mod is installed. Everything else is a pure overlay/UI addition and shouldn't conflict with anything else. Works identically on Windows and on Linux/Steam Deck via Proton.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for all changes, or the [releases page](https://github.com/h0sti81/ScheduleOneNavigator/releases) for downloadable builds.

## Credits

Created by h0sti.
