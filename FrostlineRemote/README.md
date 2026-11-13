# Frostline Remote (iOS)

iPhone als Funk-Controller für Frostline: zwei virtuelle Sticks im Modus 2,
optional Gyro-Steuerung, Haptik und Telemetrie vom Mac.

## Starten

1. `FrostlineRemote.xcodeproj` in Xcode 15 oder neuer öffnen (erzeugt mit XcodeGen aus `project.yml`).
2. Ziel „FrostlineRemote“ › Signing & Capabilities: dein Team wählen. Bei Bedarf die
   Bundle ID `app.frostline.remote` auf eine eigene ändern.
3. iPhone anschließen, Play. Beim ersten Start fragt iOS nach „Lokales Netzwerk“: erlauben.
4. Auf dem Mac Frostline starten. Die App findet den Mac per Bonjour selbst; sonst in den
   Einstellungen (Zahnrad) die IP-Adresse des Macs eingeben.

Verteilung per TestFlight: Product › Archive, dann im Organizer „Distribute App“ ›
„TestFlight & App Store“.

## Bedienung

| Element | Funktion |
| --- | --- |
| Linke Hälfte | Gas (federt nicht zurück, springt beim erneuten Anfassen nicht) und Gier |
| Rechte Hälfte | Nick und Roll mit Federrückstellung; der Stick erscheint dort, wo der Daumen aufsetzt |
| ARM | Schärfen (nur mit Gas unten, wie am echten Sender) |
| Acro / Angle / Horizon | Flugmodus |
| Turtle | gedrückt halten, um die Drohne nach einem Absturz umzudrehen |
| ↺ | Drohne zurücksetzen |
| Gyro | Nick und Roll durch Kippen; die Haltung beim Einschalten ist die Mitte |

Telemetrie oben: Verbindung, Laufzeit (ms), Akku, Tempo, Höhe, Rundenzeit.
Vibration bei Scharfschalten, Absturz, Bodenkontakt und Gates.

## Technik

- UDP mit 250 Paketen pro Sekunde (Network.framework), jedes Paket mit Sequenznummer;
  alte Pakete verwirft das Spiel. Fehlen 200 ms lang Pakete, geht die Drohne in Failsafe.
- Protokoll in `FrostlineRemoteKit` (plattformunabhängig, `swift test` läuft auch unter
  Linux). Gegenstück im Spiel: `Frostline/Assets/Frostline/Shared/Remote/RemoteProtocol.cs`.
  Beide Seiten werden gegen dieselben Byte-Testvektoren geprüft.
