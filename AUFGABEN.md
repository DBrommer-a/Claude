# Aufgaben für dich (am Mac)

> Vieles davon geht per Terminal: `tools/mac/frostline.sh alles` richtet ein, baut Insel und
> Mac-Build, lässt die Tests laufen, macht Screenshots und die fps-Messung (Ergebnisse in `Ausgabe/`).
> Vorher einmal `tools/mac/frostline.sh unity-installieren` und in Unity Hub anmelden.

Alles, was ohne deinen Mac ging, ist erledigt. Code, Szenen-Builder, iPhone-App,
Multiplayer, Concept-Art und Tests liegen im Repository. Die folgenden Schritte brauchen
deinen Mac, dein Apple- oder Unity-Konto oder deine Entscheidung.

**Stand vom Mac (13.11.2026):** `frostline.sh alles` läuft auf dem MacBook Air (M4) mit
Unity 6000.6.9f1 vom USB-Stick komplett durch: Einrichten ohne übersprungene Schritte, Insel,
102/102 Unity-Tests, Mac-Build, 19 Screenshots in `Ausgabe/bilder/`, Messung **bestanden**
(Medium, 1920 × 1080: Ø 153,5 fps, 1 %-Tief 125,8 fps, Bericht in `Ausgabe/benchmark_bericht.txt`).
Behoben dabei: mehrdeutiges `AddHDLight` (HDRP 17.6), fehlender Texturordner, verlorene
Bodenschichten (Insel war nur Sand), eigene Drohne im FPV-Bild, blockiges Analograuschen.
iPhone-App: 13/13 Swift-Tests, Simulator-Build startet.

## 1 · Projekt öffnen und Insel bauen (ca. 30 min)

- [x] Unity Hub installieren, dazu **Unity 6000.6.9f1** (Unity 6.6) mit **Mac Build Support**.
      (Liegt auf dem USB-Stick; `frostline.sh` findet Hub und Editor dort.)
- [x] In Unity Hub „Add project from disk“ → Ordner `Frostline`. Der erste Import lädt HDRP,
      Input System, Netcode, Multiplayer Services und glTFast; das dauert einige Minuten.
- [x] Menü **Frostline › 1 · Projekt einrichten**, danach Unity neu starten, wenn der Dialog es sagt.
- [x] Menü **Frostline › 4 · Insel erstellen (4 × 4 km)**. Der Bau dauert ein paar Minuten
      (vier Terrain-Tiles mit je 2049² Höhenpunkten).
- [x] Konsole ansehen: Meldungen mit „Frostline: … wird übersprungen“ bitte an mich schicken.
      Sie bedeuten, dass ein HDRP-Feld in deiner Version anders heißt. (Keine mehr.)
- [ ] Play drücken. Ohne Sender fliegst du mit der Tastatur: W/S Gas, A/D Gier, Pfeile, Leertaste schärfen.
- [x] Prüfen, ob das Higgsfield-Drohnenmodell mit der Nase nach vorn zeigt. Ja: siehe
      `Ausgabe/bilder/12_Drohne_Seite_Nase_rechts.png` (Antenne hinten links, Kamera vorn rechts).

## 2 · Sender und Gate-Messung (Woche 1)

- [ ] RadioMaster Pocket per USB-C anschließen, am Sender „USB Joystick (HID)“ wählen.
- [ ] Im Spiel **F1 › Sender › Sender kalibrieren** und den Anweisungen folgen.
- [x] **Frostline › 3 · Mac-Build erstellen**, den Build starten, Qualität „Medium“ lassen
      (Zielstufe auf dem MacBook Air), **F9**.
      Nach 60 s steht das Ergebnis da. Die Textdatei aus dem Ordner `Benchmarks` bitte an mich.
      (Bestanden, Bericht in `Ausgabe/benchmark_bericht.txt`.)
- [ ] Beim ersten Start fragt macOS nach eingehenden Verbindungen: **Erlauben**.
      Das brauchen die iPhone-App und der Multiplayer.
- [x] Offene Frage aus dem Plan: Es ist ein **MacBook Air** → Zielstufe „Medium“ (ist eingestellt).

## 3 · Gegenkandidat Yue (Woche 1)

- [ ] Im Asset Store „Yue Ultimate FPV Drone Physics“ (kostenlos) zu deinem Konto hinzufügen
      und importieren. Danach baue ich einen Adapter, damit beide Physiken dieselbe Strecke
      mit deinem Sender fliegen.

## 4 · Nach bestandenem Gate: Assets holen

Kaufen (zusammen 44,99 $ netto; €-Endpreis prüfen, über 50 € → Schneesturm durch FX Snow ersetzen):

- [ ] Modular Modern Skyscraper Vol. 1 (35 $)
- [ ] Snow storm (HDRP) (9,99 $)

Kostenlos zum Konto hinzufügen und importieren:

- [ ] Industrial Zone Modular Pack v1.0
- [ ] Slavic Medieval Village Free
- [ ] Modular Medieval Village MegaPack – Free Demo House
- [ ] Winter Mountains and Stamps
- [ ] Terrain Sample Asset Pack
- [ ] FX Snow (Plan B)
- [ ] Fab/Megascans: Felsen, Eis, Schnee, Sand, Beton, Rost, Treibholz, „Unfinished Building“,
      Cement Rubble, Brick Debris, Concrete Rubble Pile. Danach **Frostline › 5 · Megascans importieren**.
- [ ] Poly Haven: zwei bis drei Himmels-HDRIs für Menü und Fotomodus

## 5 · iPhone-App „Frostline Remote“

- [ ] Xcode 15 oder neuer, `FrostlineRemote/FrostlineRemote.xcodeproj` öffnen.
- [ ] Ziel „FrostlineRemote“ › Signing & Capabilities › dein Team wählen
      (Apple-Developer-Account). Bundle ID `app.frostline.remote` bei Bedarf ändern.
- [ ] Auf dem iPhone starten, „Lokales Netzwerk“ erlauben, Spiel auf dem Mac starten.
      Die App findet den Mac selbst; sonst Zahnrad › IP-Adresse.
- [ ] Für TestFlight: Product › Archive › Distribute App › TestFlight & App Store.

## 6 · Multiplayer

- [ ] In Unity: Edit › Project Settings › Services: mit deinem Unity-Konto anmelden und ein
      Cloud-Projekt anlegen oder verknüpfen.
- [ ] Im Unity Dashboard für das Projekt **Relay** und **Lobby** aktivieren
      (kostenlos bis 50 Spieler im Monatsmittel).
- [ ] Testen: zwei Builds (oder zwei Macs), **F2** › „Raum hosten“, Code am zweiten eingeben.

## 7 · Entscheidungen

- [x] **Higgsfield Hero-Props**: Funkmast, Leuchtturm, Schiffswrack, Kran, Fischerhütte und
      Rennen-Gates als 3D-Modelle, je 9 Credits, zusammen rund 54 Credits.
      Freigegeben und erzeugt (Tripo, detaillierte Texturen, je 7,5 Credits = 45 Credits,
      Kontostand danach **83 Credits**). Texturen auf 2K verkleinert, liegen unter
      `Models/Requisiten/`. `PropModels` setzt sie beim Inselbau in die Platzhalter ein; deren
      Kollision bleibt, Flugrouten, Ringe und Gates verhalten sich also wie vorher. Ausnahme Wrack:
      Das Modell ist ein geschlossener Rumpf, die Strandlinie führt aber durchs Rennwrack. Das
      bleibt deshalb offen, das Modell liegt als „Gestrandeter Kutter“ weiter östlich am Strand.
      Gates zeigen ihren Zustand auch am Modell (grün = nächstes, grau = durchflogen).
      Bilder: `Ausgabe/bilder/13–18_Requisite_*.png`.
- [x] Optional: Unity-Tests im Editor laufen lassen (Window › General › Test Runner › EditMode › Run All).
      (102/102 per `frostline.sh tests`.)

## Was ich hier nicht machen konnte

| Sache | Warum |
| --- | --- |
| Spiel starten, Grafik ansehen, fps messen | kein Unity-Editor und keine Grafikkarte in meiner Umgebung |
| iPhone-App bauen | braucht Xcode und das iOS-SDK (nur auf dem Mac) |
| Assets kaufen oder aus Asset Store/Fab laden | braucht dein Konto |
| Relay/Lobby einrichten | braucht dein Unity-Cloud-Projekt |
| Yue-Physik vergleichen | Paket nur über dein Asset-Store-Konto erhältlich |
