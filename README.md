# Frostline

FPV-Drohnen-Simulator in Unity 6 HDRP auf einer eisigen Insel von 4 × 4 km, mit echtem
FPV-Sender oder iPhone als Controller und Multiplayer für bis zu 8 Drohnen.

**Was du am Mac tun musst, steht in [AUFGABEN.md](AUFGABEN.md).**

## Stand

| Bereich | Stand |
| --- | --- |
| Flugphysik | fertig, 59 Tests: Motoren mit Trägheit, Akku mit Spannungseinbruch, Betaflight-/Actual-Rates, Raten-PID, Airmode, Acro/Angle/Horizon, Turtle, Failsafe, 500 Hz |
| Steuerung | USB-Sender mit Kalibrier-Assistent, Gamepad, Tastatur, iPhone-App |
| Insel | ganze Karte als spielbarer Rohbau in Originalgröße: alle Zonen, Einfluglöcher, Wald, Wolkendecke |
| Spielmodi | Freestyle mit 12 Sammelringen, 3 Rennstrecken mit Bestzeit und Ghost, Replay, Signalverlust am Rand, Checkpoints |
| Wetter | klar, tiefe Wolken, Schneesturm; Tageszeit über Physically Based Sky; Wind auf die Drohne |
| Multiplayer | Relay + Lobby mit Raum-Code, Geister-Drohnen mit Interpolation, Rennen für alle, Fangen |
| iPhone-App | SwiftUI, feste Knüppel wie am FPV-Sender, Gyro, Haptik, Telemetrie, 250 Pakete/s; im WLAN direkt, sonst per QR-Kopplung übers Internet |
| Higgsfield | Concept-Art je Zone, Drohne, Leuchtturm, Funkmast, Kran, Fischerhütte, Renn-Gate, Kutter als 3D-Modelle, Ambient-Sounds, App-Icon, Ladebildschirm |
| Optik | Freie Vorlagen von Poly Haven (CC0): Fotoscan-Böden (Sand, Moos-Tundra, Fels, Schnee, Eis), Gebäude-Materialien (Beton, Ziegel, Holz, Putz, Dach, Rost, Stein) und rund 670 Modelle (Felsen, Treibholz, Segelschiffe, Fässer, Container, Flutlicht, Laternen, Hydranten) |

Läuft auf dem Mac (MacBook Air M4, Unity 6000.6.9f1) mit allen Schritten durch; die fps-Messung
(Medium, 1080p) liegt bei rund 145 fps im Schnitt.

## Vorlagen (Poly Haven, CC0)

`tools/mac/frostline.sh vorlagen` lädt die Liste aus `tools/vorlagen/vorlagen.json` nach
`Frostline/Assets/Frostline/Vorlagen` (rund 140 MB, nicht im Repository). `alles` macht das als
ersten Schritt. Beim Inselbau werden daraus Materialien und Terrain-Schichten angelegt
(`Vorlagen.cs`) und die Insel ausgestattet (`IslandDressing.cs`): nur auf Bodenhöhe und mit Abstand
zu Gates, Flugrouten, Ringen und Checkpoints. Fehlen die Vorlagen, baut die Insel wie früher mit
gerechneten Texturen. Bäume bleiben gebaut: Poly Havens Bäume haben 0,4 bis 17 Millionen Polygone.

## Öffnen

1. **Unity 6000.6.9f1** (Unity 6.6) mit Mac Build Support installieren, Ordner `Frostline` in Unity Hub öffnen.
2. **Frostline › 1 · Projekt einrichten** (HDRP, Qualitätsstufen, 500 Hz), Unity neu starten.
3. **Frostline › 4 · Insel erstellen (4 × 4 km)**: die ganze Karte.
   Oder **2 · Prototyp-Szene** für einen schnellen Test auf 2 × 2 km.
4. Play.

Weitere Menüpunkte: **3 · Mac-Build erstellen** (für die fps-Messung) und
**5 · Megascans importieren** (Mask-Maps, Materialien, Terrain-Layer).

## Tasten

| Taste | Funktion |
| --- | --- |
| F1 | Menü: Spiel (Rennen, Wetter, Tageszeit), Sender, Rates, Drohne, Grafik, Benchmark |
| F2 | Multiplayer: Raum hosten oder per Code beitreten |
| F9 | fps-Messung (60 s fester Flug) |
| P | Replay der letzten Minuten (Leertaste Pause, ←/→ springen, ↑/↓ Tempo, C Kamera, S speichern) |
| R | zurück zum letzten Checkpoint |
| Tastatur-Flug | W/S Gas, A/D Gier, Pfeile Nick/Roll, Leertaste schärfen, M Modus, T Turtle |

## iPhone übers Internet (ohne gleiches WLAN)

1. Einmal einen Frostline Relay auf einem Server mit öffentlicher IP einrichten:
   `tools/relay/einrichten.sh root@<server>` – danach `python3 tools/relay/pruefen.py <server>`.
   Nötig ist nur UDP-Port 47821 (in der Firewall des Servers bzw. Hosters öffnen).
2. Im Spiel F1 › Spiel › „Übers Internet“ den Server eintragen. Es erscheint ein QR-Code.
3. Den QR-Code einmal mit der iPhone-Kamera scannen (oder in der App unter Setup › „QR-Code scannen“).
   Die Kopplung liegt danach im Schlüsselbund; die App verbindet sich von selbst – im selben WLAN direkt,
   sonst über den Server, auch über Mobilfunk.

Sicherheit: Der QR-Code enthält einen zufälligen 256-Bit-Schlüssel. Jedes Paket ist signiert
(HMAC-SHA-256); das Spiel nimmt nur Pakete des gekoppelten Handys an. Der Server kennt den Schlüssel
nicht und reicht nur weiter. „Neuen Code erzeugen“ im Spiel meldet ein gekoppeltes Handy ab.

## Die Insel

| Zone | Zum Reinfliegen |
| --- | --- |
| Strand (Start) | Schiffswrack auf der Seite, an beiden Enden offen |
| Hafen, Fischerdorf | offene Lagerhalle, unter dem Pier durch, Leuchtturm-Galerie |
| Industriegebiet | drei Hallen mit eingestürzten Dachfeldern und hängenden Bindern, Förderband-Tunnel, Schornstein (oben rein, unten raus), Kranbahn |
| Stadt | Rohbau-Hochhaus mit offenen Etagen und Gerüst, Lücken zwischen acht Türmen |
| Tal-Dorf, Bergdorf | offene Scheunen, Glockenstuhl, Steinbrücke |
| Gletscher | Eistunnel in der Gletscherzunge, Felsbögen, Talabfahrt von 640 auf 110 m |
| Wald | Baum-Slalom, unter umgestürzten Stämmen durch |
| Wolkendecke | ab 650 m, die Gipfel ragen hinein |

Rennstrecken: **Strandlinie** (durchs Wrack), **Industrie-Slalom** (durch Hallen und Tunnel),
**Gletscher-Dive** (durch Eistunnel und Felsbogen).

## Aufbau

| Ordner | Inhalt |
| --- | --- |
| `Frostline/Assets/Frostline/Flight/Core` | Flugphysik ohne Unity-Abhängigkeit (nach KestrelFPV, MIT) |
| `Frostline/Assets/Frostline/Shared` | testbare Logik: Funkprotokoll, Rennen, Replay, Signal, Interpolation, Mask-Map |
| `Frostline/Assets/Frostline/Runtime` | Unity-Anbindung: Drohne, Eingabe, iPhone-Empfang, Spielmodi, Wetter, Menü, OSD, Ton |
| `Frostline/Assets/Frostline/Multiplayer` | Netcode for GameObjects, Sessions (Relay + Lobby), Geister-Drohnen |
| `Frostline/Assets/Frostline/Editor` | Projekt-Einrichtung, Szenen- und Insel-Builder, Mac-Build, Megascans-Import |
| `Frostline/Assets/Frostline/Tests` | 102 Tests (Unity Test Runner oder `dotnet test tools/flightcore-tests`) |
| `FrostlineRemote` | iPhone-App mit Xcode-Projekt; `FrostlineRemoteKit` mit 24 Swift-Tests |
| `tools/relay` | Frostline Relay: kleiner UDP-Server für die iPhone-Steuerung übers Internet (Python, ohne Abhängigkeiten) |
| `Design/Concept` | Concept-Art aus Higgsfield als Ziel für Licht und Farben |

Bei jedem Push laufen die C#- und Swift-Tests automatisch (GitHub Actions).

## Flugphysik in Zahlen (Preset „Freestyle 5"“)

| Messung | Wert |
| --- | --- |
| Gewicht / Schub-Gewicht | 0,70 kg / 8,5 |
| Gas zum Schweben | 28 % |
| Strom Schweben / Vollgas | ca. 6 A / ca. 125 A, Spannung bricht von 25,2 auf ca. 21 V ein |
| Steiggeschwindigkeit Vollgas | ca. 120 km/h |
| Roll-Sprung auf 207 °/s | 10–90 % in ca. 40 ms, 5 % Überschwingen |
| Rates (Standard) | Betaflight RC Rate 1,00 · Super Rate 0,70 → 667 °/s |
