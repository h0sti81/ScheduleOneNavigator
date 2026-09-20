# Changelog

Alle wichtigen Änderungen an diesem Mod werden hier festgehalten. Format lose
angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

## [Unreleased]

- **Dealer-Anzeige überarbeitet**: Der "Dealers"-Tab zeigte bisher die falschen
  NPCs (Shopkeeper wie Stan/Oscar statt der echten anwerbbaren Dealer). Zeigt
  jetzt Benji/Brad/Jane/Leo/Molly/Wei; die Shopkeeper sind stattdessen im
  Shops-Tab zu finden. Neue dritte Statusfarbe (grau) für "freigeschaltet, aber
  noch nicht angeheuert", zusätzlich zu gesperrt/angeheuert.

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

[Unreleased]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.5...HEAD
[v0.5]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.4...v0.5
[v0.4]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.3...v0.4
[v0.3]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.2...v0.3
[v0.2]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.1...v0.2
[v0.1]: https://github.com/h0sti81/ScheduleOneNavigator/releases/tag/v0.1
