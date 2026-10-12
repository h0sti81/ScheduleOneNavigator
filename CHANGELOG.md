# Changelog

Alle wichtigen Änderungen an diesem Mod werden hier festgehalten. Format lose
angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.0.0/).

## [Unreleased]

- **Crash-Info-Sammler** `tools/CollectCrashInfo.bat`: Doppelklick sammelt
  MelonLoader-Logs, Unity-Player.log und Crash-Reports, Windows-Absturzereignisse,
  Mod-Liste, Spielversion/Branch und Systeminfos in ein ZIP auf dem Desktop
  (Windows-Benutzername wird aus den Logs entfernt). Für Bug-Reports.

## [v0.7.4] - 2026-10-11

- **TAB-Eigenheit behoben**: wurde die Karte mit M geöffnet und wieder
  geschlossen, öffnete TAB danach die alte Spielkarte statt des Handys - so
  lange, bis eine Karte per Rechtsklick geschlossen wurde. Ursache: beim
  Schließen des Handys blieb die Karten-App darin geöffnet. Sie wird jetzt mit
  geschlossen, TAB öffnet wieder zuverlässig den Handy-Homescreen.
- Nach Esc/Rechtsklick und anschließendem Schließen per TAB werden Bewegung
  und Ausrüstung jetzt zuverlässig wieder freigegeben.
- README: Bedienung korrigiert (M bzw. Map-App öffnet die Karte, M/TAB
  schließt, Esc/Rechtsklick zurück zum Handy).

## [v0.7.3] - 2026-10-11

- **Optimale Route rechnet mit dem echten Vorrat**: bisher wurde jeder Deal
  einzeln gegen das Inventar geprüft - mit 5 Stück galten sechs Deals als
  erfüllbar, die zusammen 17 brauchten. Jetzt wird nur geplant, was der Vorrat
  gemeinsam abdeckt. Liste zeigt "in route" / "not enough stock" /
  "missing product", Button z. B. "Optimal route (2 of 6)".
- **Priorität**: zuerst Deals, die als Nächstes ablaufen, dann kurze Wege vor
  Profit - immer der nächste Kunde, den der restliche Vorrat noch abdeckt.
  Restzeit steht jetzt bei jedem Deal ("3h left").
- **Ganze Tour sichtbar**: alle Stopps nummeriert auf der Karte, spätere
  Etappen als blasse Linie (vorher war nur der nächste Kunde zu sehen).
- **Route plant sich laufend neu** (alle 1,5 s und nach jeder Übergabe):
  Nachladen, neue oder abgelaufene Deals werden automatisch berücksichtigt;
  das aktuelle Ziel bleibt dabei stabil. Weitergeschaltet wird erst nach der
  tatsächlichen Übergabe, nicht schon beim Ankommen.
- **Ankunft im Fahrzeug** zählt ab 8 m statt 3 m.
- **Sewer Office ohne Schlüssel** steht jetzt unter "Locked" (ohne Marker,
  nicht anklickbar) statt als Eigentum.

## [v0.7.2] - 2026-10-11

- **Biz-Tab unterscheidet jetzt gekauft/nicht gekauft**: bisher wurden alle
  Businesses des Spiels als eigene angezeigt, auch noch nicht gekaufte (z. B.
  Hyland Medical). Jetzt gruppiert in "Owned" und "For sale" (mit Preis, nicht
  anklickbar), Kartenmarker nur noch für gekaufte - wie im Properties-Tab.
- **Supplier "(out of town)" nicht mehr anklickbar**: sie haben keinen
  Standort, zu dem eine Route führen könnte.
- **Keine Routen mehr über Dächer**: Übergänge zwischen Rasterzellen hatten
  keine maximale Stufenhöhe - vom Dach "fallen" ist kollisionsfrei, und da
  Übergänge in beide Richtungen gelten, führten Routen über flache Dächer
  quer durch Gebäude. Jetzt darf jeder Teilschritt nur so viel steigen/fallen,
  wie die Spielfigur wirklich schafft (Stufenhöhe bzw. Steigung).
- **Routen meiden Gebäude**: Zellen in Gebäuden (erkannt über ein Dach darüber
  oder die Gebäudefarbe der Spielkarte) kosten 10x so viel - Routen bleiben
  auf Straßen und Gehwegen und gehen nur noch hinein, wenn das Ziel drinnen
  liegt oder es keinen anderen Weg gibt. Vorher führten Routen z. B. quer
  durch das Lagerhaus an den Docks.
- **Diagonalschritte symmetrisch geprüft**: ein Schritt von A nach B wurde
  anders bewertet als von B nach A. Dadurch fiel die Ausweich-Route (Spieler in
  einem abgeschlossenen Raum) still auf den kürzesten Zellen-Weg zurück und
  ignorierte Straßen- und Gebäudekosten.
- **Abgeschnittene Suchbereiche erkannt**: klebt eine Route am Rand des
  abgetasteten Bereichs oder beginnt die Ausweich-Route weit vom Spieler
  entfernt, wird einmal ein größerer Bereich abgetastet (vorher z. B. Route
  zum Bungalow ab einem Steg statt ab den Docks).
- **Debug-Dump**: mit `VerboseLogging = true` wird für jedes neue Ziel das
  Wegfindungs-Raster nach `UserData/NavigatorDebug/` geschrieben (für
  Fehlerberichte).

## [v0.7.1] - 2026-10-11

- **Ruhiges Log**: die ausführlichen Diagnose-Ausgaben (Routen-Statistik alle
  1,5 s, Klick-/Tab-Diagnosen, Inventarliste beim Öffnen des Handys) sind jetzt
  standardmäßig aus. Für Fehlerberichte einschaltbar über
  `VerboseLogging = true` in der Kategorie `[ScheduleOneNavigator]` von
  `UserData/MelonPreferences.cfg`.
- **Oberfläche komplett auf Englisch**: Kartentitel, Listenüberschriften
  (Current/Scheduled, Owned/For sale), Deal-Status, Routen-Button, Hinweiszeile
  und Dealer-Status.
- **Ruckler bei offener Karte behoben**: die Listen im linken Bereich wurden
  jede Sekunde komplett zerstört und neu aufgebaut (inkl. Neuaufbau der
  ganzen Oberfläche), im Shops-Tab dazu vier Szenen-Suchen pro Sekunde.
  Listen werden jetzt nur noch neu aufgebaut, wenn sich ihr Inhalt ändert,
  und die Szenen-Suchen werden zwischengespeichert.
- **Schnellere Routensuche**: die A*-Suche nutzt jetzt eine
  Prioritätswarteschlange statt einer linear durchsuchten Liste (3-8x
  schneller, besonders im Fahrzeugmodus).

## [v0.7] - 2026-10-11

- **Fahrzeugmodus für die Route**: sitzt man in einem Fahrzeug, bevorzugt die
  Route Straßen jetzt stark - kürzester Weg auf die Straße, dann auf der
  Straße bis nahe ans Ziel, statt quer über Wiesen, Piers und zwischen
  Gebäuden hindurch. Zu Fuß bleibt es bei der bisherigen leichten
  Straßenbevorzugung. Der Wechsel wird automatisch erkannt.
- **Ausweich-Route beachtet jetzt Straßen**: stand der Spieler (oder sein
  Fahrzeug) in einem abgeschnittenen Bereich, wurde die Route vom nächsten
  erreichbaren Punkt aus nur als "wenigste Zellen"-Weg gezogen und ignorierte
  Straßen komplett. Läuft jetzt über dieselbe gewichtete Suche wie die normale
  Route.
- **Oscar's Store wieder erreichbar**: der Zielpunkt rastete auf einem kleinen,
  abgeschnittenen Absatz neben dem Laden ein, sodass nie eine Route gefunden
  wurde. Das Einrasten überspringt jetzt solche Mini-Inseln.
- **Lila Punkte auf der Minimap entfernt**: die Minimap-Kamera zeichnete auch
  Layer, die das Spiel selbst ausblendet (Hilfsobjekte an Bäumen erschienen als
  lila Punkte). Sie übernimmt jetzt die Layer-Maske der Spielerkamera.

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
  statt über einen eigenen Hotkey - behebt nebenbei einen seit langem
  bestehenden Bug, bei dem ein Klick bei offenem Tablet versehentlich nahe
  NPCs angegriffen hat.
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

[Unreleased]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.7.4...HEAD
[v0.7.4]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.7.3...v0.7.4
[v0.7.3]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.7.2...v0.7.3
[v0.7.2]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.7.1...v0.7.2
[v0.7.1]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.7...v0.7.1
[v0.7]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.6...v0.7
[v0.6]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.5...v0.6
[v0.5]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.4...v0.5
[v0.4]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.3...v0.4
[v0.3]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.2...v0.3
[v0.2]: https://github.com/h0sti81/ScheduleOneNavigator/compare/v0.1...v0.2
[v0.1]: https://github.com/h0sti81/ScheduleOneNavigator/releases/tag/v0.1
