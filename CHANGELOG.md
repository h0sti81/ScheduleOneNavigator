# Changelog

Alle wichtigen Änderungen an diesem Mod werden hier festgehalten. Format lose
angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

## [Unreleased]

## [v0.6] - 2026-09-21

- **Dealer-Anzeige überarbeitet**: Der "Dealers"-Tab zeigte bisher die falschen
  NPCs (Shopkeeper wie Stan/Oscar statt der echten anwerbbaren Dealer). Zeigt
  jetzt Benji/Brad/Jane/Leo/Molly/Wei; die Shopkeeper sind stattdessen im
  Shops-Tab zu finden. Neue dritte Statusfarbe (grau) für "freigeschaltet, aber
  noch nicht angeheuert", zusätzlich zu gesperrt/angeheuert. Unfertige,
  generische "Benzies Dealer"-Platzhaltereinträge werden nicht mehr angezeigt.
- **Fahrzeug-Routing repariert**: die Route folgte im Fahrzeug nicht mehr der
  tatsächlichen Position (blieb am Einstiegspunkt hängen), Ankunftserkennung
  war ebenfalls betroffen - beides behoben.
- **Andere Spieler auf jedem Tablet-Tab sichtbar**: grüner Punkt mit
  Namensbeschriftung, wie schon auf der Minimap.
- **Rekrutierbare Kunden überarbeitet**: Minimap und Tablet-Deals-Tab zeigen
  die rote Aura jetzt nur noch für Kunden, die das Spiel gerade wirklich als
  rekrutierbar einstuft (`Customer.IsUnlockable()`) - vorher wurden auch
  Kunden markiert, die noch ein Standing/eine Anforderung brauchen. Im Tablet
  zusätzlich anklickbar (Auto-Routing dorthin).
- **Tablet ist jetzt eine echte Handy-App**: öffnet über das normale Handy
  (neues "N"-Icon in der App-Übersicht) statt über einen eigenen Hotkey -
  behebt nebenbei einen seit langem bestehenden Bug, bei dem ein Klick bei
  offenem Tablet versehentlich nahe NPCs angegriffen hat.
- **Cursor-Bug behoben**: der Mauszeiger blieb nach dem Schließen des Handys
  manchmal frei/aktiv, statt wieder gesperrt zu werden - das ließ auch das
  linke Tab-Menü (Deals/Shops/Biz/Dealers/Eigentum) gelegentlich nicht mehr
  auf Klicks reagieren.
- **TAB schließt jetzt zuverlässig auch die Kartenansicht**, genau wie es das
  Handy selbst schließt - vorher blieb die Karte nach TAB manchmal offen
  hängen.
- **Diverse Shop-/Eigentums-Zielpunkte korrigiert**: Gas-Mart (Central),
  Thrifty Threads, Bleuball's Boutique, Hyland Manor, Sewer Office, Docks
  Warehouse und Sweatshop routen jetzt zuverlässig zu einem erreichbaren
  Zielpunkt (vorher teils falsche oder unerreichbare Koordinaten).

## [v0.5] - 2026-09-19

- Rekrutierbare-Kunden-Marker korrekt gefiltert (`Customer.IsUnlockable()`):
  nur noch NPCs, die das Spiel gerade wirklich als rekrutierbar einstuft,
  bekommen die rote Aura (Minimap und Tablet-Deals-Tab). Vorher wurden auch
  NPCs angezeigt, die noch ein Standing/eine Anforderung brauchen.

## [v0.4] - 2026-09-19

- Rekrutierbare Kunden jetzt auch im Tablet sichtbar (Deals-Tab): rote Aura +
  Punkt, wie bisher schon auf der kleinen Minimap.
- Anklickbar: ein Klick auf einen rekrutierbaren NPC setzt eine Route dorthin.
- Minimap: fehlender roter Punkt in der Aura wieder ergänzt.

## [v0.3] - 2026-09-18

- Tablet als echte Handy-App statt eigenem Hotkey (`#`): öffnet jetzt über die
  normale Handy-Kartenkachel (umgelabeltes "N"-Icon).
- Damit auch der Bug behoben, dass ein Klick bei offenem Tablet versehentlich
  nahe NPCs angegriffen hat.

## [v0.2] - 2026-09-18

- Routing im Fahrzeug repariert: folgt jetzt der tatsächlichen
  Fahrzeugposition statt am Einstiegspunkt hängen zu bleiben (inkl.
  Ankunftserkennung).
- Andere Spieler im Tablet sichtbar: grüner Punkt mit Namensbeschriftung, auf
  jedem Tab.
- Minimap-Kundenanzeige überarbeitet: rote Aura markiert jetzt noch
  rekrutierbare NPCs statt bereits rekrutierter.

## [v0.1] - 2026-09-18

- Erstes Release: kleine Minimap (Kamera, Spielerpfeil, andere Spieler,
  Kunden-Aura), Tablet-Overlay mit Tabs Deals/Shops/Businesses/Dealers/
  Eigentum, Auto-Routing über eigenes TerrainGrid-Pathfinding.

[Unreleased]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.6...HEAD
[v0.6]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.5...v0.6
[v0.5]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.4...v0.5
[v0.4]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.3...v0.4
[v0.3]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.2...v0.3
[v0.2]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.1...v0.2
[v0.1]: https://github.com/h0sti81/ScheduleOneNavigator/releases/tag/v0.1
