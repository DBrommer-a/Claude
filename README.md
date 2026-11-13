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
| iPhone-App | SwiftUI, Multitouch-Sticks, Gyro, Haptik, Telemetrie, 250 Pakete/s |
| Higgsfield | Concept-Art je Zone, Testdrohne als 3D-Modell, Ambient-Sounds, App-Icon, Ladebildschirm |
| Optik | Platzhalter-Texturen und Rohbau-Geometrie; die echten Assets kommen nach dem 60-fps-Gate |

Alles ist gegen Unity 6.3 kompiliert, aber noch nicht im Editor gestartet worden.

## Öffnen

1. **Unity 6000.3.28f1** mit Mac Build Support installieren, Ordner `Frostline` in Unity Hub öffnen.
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
| `FrostlineRemote` | iPhone-App mit Xcode-Projekt; `FrostlineRemoteKit` mit 13 Swift-Tests |
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
