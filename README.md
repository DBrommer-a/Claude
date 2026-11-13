# Frostline

FPV-Drohnen-Simulator in Unity 6 HDRP auf einer eisigen Insel. Dieses Repository
enthält den Stand für **Woche 1–2 (Prototyp)** aus dem Gesamtplan: Flugphysik,
Sender-Eingabe, Ozean, Volumenwolken, ein Terrain-Tile und die fps-Messung für
das erste Gate („60 fps im Build, Physik gewählt, dann kaufen“).

## Öffnen

1. Unity Hub installieren und **Unity 6000.3.28f1 (Unity 6.3 LTS)** mit dem
   Modul **Mac Build Support** hinzufügen. Eine neuere 6000.3-Version geht auch.
2. In Unity Hub „Add project from disk“ → den Ordner `Frostline` wählen.
   Beim ersten Öffnen lädt Unity HDRP und das Input System; das dauert einige Minuten.
3. Menü **Frostline › 1 · Projekt einrichten**. Richtet HDRP mit den
   Qualitätsstufen Low/Medium/High/Ultra ein (High: 67 % Auflösung + STP),
   aktiviert Wasser und Wolken und stellt die Physik auf 500 Hz.
   Danach Unity einmal neu starten, wenn der Dialog darum bittet.
4. Menü **Frostline › 2 · Prototyp-Szene erstellen**. Baut die Szene
   `Assets/Frostline/Scenes/Prototype.unity` komplett per Skript.
5. **Play** drücken.

## Steuerung

| Gerät | So geht's |
| --- | --- |
| FPV-Sender (z. B. RadioMaster Pocket, EdgeTX) | Per USB-C anschließen, am Sender „USB Joystick (HID)“ wählen, HF-Module aus. Im Spiel F1 → „Sender kalibrieren“. Der Assistent erkennt Kanalreihenfolge und Richtung selbst. |
| Gamepad (PS5, Xbox) | Linker Stick Gas/Gier, rechter Stick Nick/Roll. A/✕ schärfen, Y/△ Modus, X/□ Reset, B/○ Turtle. |
| Tastatur (Notlösung) | W/S Gas, A/D Gier, Pfeiltasten Nick/Roll, Leertaste schärfen, M Modus, R Reset, T Turtle. |

**F1** Menü (Sender, Rates, Drohne, Grafik, Benchmark) · **F9** fps-Messung · **R** zurück zum Startplatz.

Geschärft wird nur mit Gas ganz unten, wie bei einer echten Drohne.

## Das 60-fps-Gate messen

1. **Frostline › 3 · Mac-Build erstellen** (Apple Silicon, kein Development Build).
2. Den Build starten, Qualitätsstufe „High“ lassen, **F9** drücken.
3. Nach 60 Sekunden automatischem Flug über Meer, Insel und durch die Wolken
   erscheint das Ergebnis. Bestanden ist es, wenn auch die langsamsten 1 % der
   Bilder über 60 fps liegen. Der Bericht (Text + jedes einzelne Bild als CSV)
   liegt unter `~/Library/Application Support/Frostline/Frostline/Benchmarks/`;
   der genaue Pfad steht auch im Menü unter „Benchmark“.

Im Editor gemessene Werte zählen nicht; der Editor ist auf demselben Mac deutlich langsamer.

## Aufbau

| Ordner | Inhalt |
| --- | --- |
| `Assets/Frostline/Flight/Core` | Flugphysik ohne Unity-Abhängigkeit: Motoren, Propeller, Akku, Luftwiderstand, Raten-PID, Mixer mit Airmode, Betaflight- und Actual-Rates, Acro/Angle/Horizon, Turtle, Failsafe. Basiert auf KestrelFPV (MIT). |
| `Assets/Frostline/Runtime` | Unity-Anbindung: `DroneController` (Rigidbody, 500 Hz), `StickInput` (Sender, Gamepad, Tastatur), Kalibrier-Assistent, OSD, Menü, prozeduraler Motorsound, Benchmark. |
| `Assets/Frostline/Editor` | Projekt-Einrichtung, Szenen-Builder, Insel-Generator, Mac-Build. |
| `Assets/Frostline/Tests` | 59 Tests der Flugphysik (Unity Test Runner oder `dotnet test`). |
| `tools/flightcore-tests` | Führt dieselben Tests ohne Unity aus: `dotnet test` (.NET 8). |

## Flugphysik in Zahlen (Preset „Freestyle 5"“)

| Messung | Wert |
| --- | --- |
| Gewicht / Schub-Gewicht | 0,70 kg / 8,5 |
| Gas zum Schweben | 28 % |
| Strom Schweben / Vollgas | ca. 6 A / ca. 125 A, Spannung bricht von 25,2 auf ca. 21 V ein |
| Steiggeschwindigkeit Vollgas | ca. 120 km/h |
| Roll-Sprung auf 207 °/s | 10–90 % in ca. 40 ms, 5 % Überschwingen |
| Rates (Standard) | Betaflight RC Rate 1,00 · Super Rate 0,70 → 667 °/s |

Weitere Presets: Racer 5", Cinewhoop 3", Bebop (Original-Werte aus KestrelFPV).

## Noch offen für Woche 1–2

- **Gegenkandidat Yue**: „Yue Ultimate FPV Drone Physics“ ist kostenlos, muss aber
  von dir im Asset Store geholt werden. Dann fliegen beide dieselbe Strecke mit
  deinem Sender; das bessere Stickgefühl gewinnt.
- **Gate-Messung auf deinem M4**: kann nur auf deinem Mac laufen (siehe oben).
- Erst nach bestandenem Gate werden die beiden Kauf-Assets geholt.

Terrain-Texturen und Drohnenmodell sind Platzhalter; Megascans und das
Higgsfield-Modell kommen in den späteren Phasen.
