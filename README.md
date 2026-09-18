# ScheduleOne Navigator

A full-screen, self-contained map and navigation overlay for **Schedule I**, built on top of [MelonLoader](https://github.com/LavaGang/MelonLoader). Press `#` to open a tablet-style map with click-to-route pathfinding and tabs for your active deals, shops, businesses, and dealers/suppliers — plus a small always-on minimap in the corner of the HUD.

## Features

- **Minimap** (top-right HUD): live top-down view centered on the player, with markers for your properties/businesses and unlocked customers.
- **Full map overlay** (`#` to toggle): pan and click anywhere to set a route, shown as a dotted line and followed on the minimap.
- **Deals tab**: your currently active delivery contracts, with an "optimal route" button that orders all fulfillable deals into a single efficient route based on what's in your inventory.
- **Shops tab**: real in-world retail locations (gas stations, hardware stores, clothing, pawn shop, casino, barbershop, tattoo parlor, auto dealerships, ...), click a marker or list entry to route to it.
- **Businesses tab**: your production properties (warehouse, sweatshop, motel, etc.).
- **Dealers & Suppliers tab**: every dealer and supplier NPC, grouped and sorted, with a clear visual indicator for which ones you've already unlocked.

## Requirements

- **Schedule I** (Steam)
- **MelonLoader**, version 0.7.3 or newer (Open Beta / Il2Cpp build) — [official repository](https://github.com/LavaGang/MelonLoader)

MelonLoader works the same way whether you're on Windows or on Linux/Steam Deck via Proton — install it first if you haven't already, following the instructions on the MelonLoader repository.

## Installation

1. Make sure MelonLoader is installed for Schedule I, and start the game at least once with it so the `Mods` folder gets created.
2. Download `ScheduleOneNavigator.dll` from this mod page.
3. Open your Schedule I install folder:
   - **Steam (Windows/Linux/Steam Deck):** right-click **Schedule I** in your Steam library → **Manage** → **Browse local files**.
4. Copy `ScheduleOneNavigator.dll` into the `Mods` subfolder (create it if it's missing — it usually already exists after step 1).
5. Launch the game normally.

## Usage

- Press `#` to open or close the full map overlay.
- Click a tab (Deals / Shops / Businesses / Dealers) to switch views.
- Click anywhere on the map, or on a marker/list row, to set a route there. Press `#` or `Esc` to close.
- Right-click-drag to pan the map.

## Uninstalling

Delete `ScheduleOneNavigator.dll` from the `Mods` folder.

## Compatibility

Pure overlay/UI mod — it doesn't modify game logic, save data, or other mods' files, and shouldn't conflict with anything else. Works identically on Windows and on Linux/Steam Deck via Proton.

## Credits

Created by h0sti.
