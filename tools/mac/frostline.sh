#!/bin/bash
# Frostline auf dem Mac per Terminal: einrichten, Insel bauen, Mac-Build, Tests, Messung.
# Aufruf aus dem Repository-Ordner, z. B.:  tools/mac/frostline.sh alles
set -euo pipefail

VERSION="${UNITY_VERSION:-6000.6.9f1}"
CHANGESET="${UNITY_CHANGESET:-8c45277fb9c5}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$ROOT/Frostline"
OUT="$ROOT/Ausgabe"

# Unity Hub und die Editoren dürfen auch auf einem externen Laufwerk liegen.
# Hub: HUB_APP, sonst /Applications, sonst per Spotlight gesucht.
find_hub_app() {
  if [ -n "${HUB_APP:-}" ]; then echo "$HUB_APP"; return; fi
  if [ -d "/Applications/Unity Hub.app" ]; then echo "/Applications/Unity Hub.app"; return; fi
  mdfind "kMDItemCFBundleIdentifier == 'com.unity3d.unityhub'" 2>/dev/null | head -1
}
# Editor: UNITY, sonst der Installationsordner aus den Hub-Einstellungen, sonst die Voreinstellung.
find_unity() {
  if [ -n "${UNITY:-}" ]; then echo "$UNITY"; return; fi
  local settings="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
  local dir=""
  [ -f "$settings" ] && dir="$(sed -e 's/^"//' -e 's/"$//' "$settings")"
  for base in "$dir" "/Applications/Unity/Hub/Editor"; do
    [ -n "$base" ] && [ -x "$base/$VERSION/Unity.app/Contents/MacOS/Unity" ] \
      && { echo "$base/$VERSION/Unity.app/Contents/MacOS/Unity"; return; }
  done
  echo "/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
}
HUB_APP="$(find_hub_app)"
HUB="$HUB_APP/Contents/MacOS/Unity Hub"
UNITY="$(find_unity)"
APP="$PROJECT/Builds/Frostline.app"
QUALITY="${QUALITY:-Medium}"

mkdir -p "$OUT"

need_unity() {
  if [ ! -x "$UNITY" ]; then
    echo "Unity $VERSION fehlt. Zuerst: $0 unity-installieren" >&2
    exit 1
  fi
}

unity_batch() {
  local method="$1"; shift
  local log="$OUT/unity_${method##*.}.log"
  need_unity
  echo "▶ Unity: $method  (Log: $log)"
  "$UNITY" -batchmode -quit -projectPath "$PROJECT" -executeMethod "$method" -logFile "$log" "$@" \
    || { echo "✗ fehlgeschlagen, letzte Zeilen:"; tail -40 "$log"; exit 1; }
  grep -E "Frostline: .*(wird übersprungen|fehlgeschlagen)" "$log" || true
  echo "✓ $method"
}

player() {
  local exe="$APP/Contents/MacOS/Frostline"
  [ -x "$exe" ] || { echo "Kein Build. Zuerst: $0 build" >&2; exit 1; }
  "$exe" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -logFile "$OUT/player.log" "$@"
  grep "FROSTLINE_AUTOMATION" "$OUT/player.log" || true
}

case "${1:-hilfe}" in
  unity-installieren)
    if [ -z "$HUB_APP" ] || [ ! -d "$HUB_APP" ]; then
      command -v brew >/dev/null || { echo "Homebrew fehlt: https://brew.sh" >&2; exit 1; }
      brew install --cask unity-hub
      HUB="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"
    fi
    if [ -x "$UNITY" ]; then echo "✓ Unity $VERSION ist schon installiert: $UNITY"; exit 0; fi
    "$HUB" -- --headless install --version "$VERSION" --changeset "$CHANGESET" --architecture arm64
    echo "Danach einmal Unity Hub öffnen, mit dem Unity-Konto anmelden und die Personal-Lizenz aktivieren."
    ;;
  vorlagen)
    echo "▶ Freie Vorlagen laden (Poly Haven, CC0)"
    python3 -I "$ROOT/tools/vorlagen/vorlagen_laden.py"
    ;;
  assetstore)
    echo "▶ Heruntergeladene Asset-Store-Pakete entpacken (nur Auswahl, nicht im Repository)"
    python3 -I "$ROOT/tools/assetstore/assetstore_laden.py"
    ;;
  einrichten)
    unity_batch Frostline.EditorTools.FrostlineCommandLine.Setup
    # Nach dem Umstellen der Eingabe-Einstellung muss Unity neu starten: der nächste Aufruf ist ein neuer Prozess.
    ;;
  prototyp)
    unity_batch Frostline.EditorTools.FrostlineCommandLine.BuildPrototype
    ;;
  insel)
    unity_batch Frostline.EditorTools.FrostlineCommandLine.BuildIsland
    ;;
  spielbar)
    # Repariert die Atmosphäre der Verlassenen Insel und setzt Drohne, Spielsysteme und Kollision ein.
    unity_batch Frostline.FotoInsel.FotoSpielbar.Spielbar
    ;;
  blender)
    # DJI-FPV-Modell (Fab-Download) mit Blender in GLB wandeln. Blender, Einstellungen und
    # Zwischendateien liegen auf dem Stick; nur das fertige GLB kommt ins Projekt (nicht ins Repository).
    STICK=/Volumes/MAC-SKILLS/tools/blender
    [ -x "$STICK/Blender.app/Contents/MacOS/Blender" ] || { echo "Blender fehlt: $STICK – ist der Stick eingesteckt?" >&2; exit 1; }
    ZIP="${DJI_ZIP:-$HOME/Downloads/dji-fpv-by-sdc-high-performance-drone.zip}"
    WORK="$STICK/modelle/dji_fpv"
    mkdir -p "$WORK" "$STICK/tmp" "$STICK/benutzer"
    [ -f "$WORK/source/DJI FVP.blend" ] || unzip -o -q "$ZIP" -d "$WORK"
    # --disable-autoexec: Skripte in der fremden .blend-Datei laufen nicht.
    TMPDIR="$STICK/tmp" BLENDER_USER_RESOURCES="$STICK/benutzer" "$STICK/Blender.app/Contents/MacOS/Blender" \
      -b --factory-startup --disable-autoexec "$WORK/source/DJI FVP.blend" \
      --python "$ROOT/tools/blender/blend_zu_glb.py" -- "$WORK/DJI_FPV.glb" --texturen "$WORK/textures"
    mkdir -p "$PROJECT/Assets/Frostline/Models/DJI"
    cp "$WORK/DJI_FPV.glb" "$PROJECT/Assets/Frostline/Models/DJI/"
    echo "✓ DJI_FPV.glb im Projekt – danach: $0 spielbar"
    ;;
  pruefen)
    unity_batch Frostline.EditorTools.VerlasseneInselPruefung.Pruefen
    cat "$OUT/pruefung/bericht.txt"
    ;;
  build)
    unity_batch Frostline.EditorTools.FrostlineCommandLine.BuildMac -frostlineOutput "$APP"
    ;;
  tests)
    need_unity
    echo "▶ Unity-Tests (EditMode)"
    "$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform EditMode \
      -testResults "$OUT/tests.xml" -logFile "$OUT/unity_tests.log" || true
    grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/tests.xml" | head -1 || tail -30 "$OUT/unity_tests.log"
    ;;
  bilder)
    rm -rf "$OUT/bilder"
    player -frostlineShots "$OUT/bilder" -frostlineQuality "$QUALITY"
    ls "$OUT/bilder"
    ;;
  messung)
    player -frostlineBenchmark -frostlineQuality "$QUALITY" -frostlineResult "$OUT/benchmark.json"
    cat "$OUT/benchmark.json"
    ;;
  multiplayer)
    # Zwei Spiele auf diesem Mac: eins hostet (Unity Relay), das zweite tritt mit dem Raum-Code bei,
    # prüft die Host-Drohne und die vom Host gesetzte Lichtstimmung und macht ein Bild.
    exe="$APP/Contents/MacOS/Frostline"
    [ -x "$exe" ] || { echo "Kein Build. Zuerst: $0 build" >&2; exit 1; }
    code="$OUT/mp_code.txt"
    rm -f "$code"
    mkdir -p "$OUT/bilder_mp"
    "$exe" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$OUT/mp_host.log" \
      -frostlineQuality Low -frostlineHost "$code" -frostlineMpDauer 200 &
    host=$!
    gast=0
    "$exe" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$OUT/mp_gast.log" \
      -frostlineQuality Medium -frostlineJoin "$code" -frostlineMpBild "$OUT/bilder_mp/Mitspieler_sieht_Host.png" || gast=$?
    sleep 4
    kill "$host" 2>/dev/null || true
    wait "$host" 2>/dev/null || true
    grep -h "FROSTLINE_MP" "$OUT/mp_host.log" "$OUT/mp_gast.log" || true
    # Der Rückgabewert des macOS-Players ist nicht verlässlich; maßgeblich ist die Prüfzeile im Log.
    if grep -q "FROSTLINE_MP OK" "$OUT/mp_gast.log"; then echo "✓ Multiplayer"; else echo "✗ Multiplayer (Gast-Code $gast)"; exit 1; fi
    ;;
  ios-pruefen)
    xcodebuild -project "$ROOT/FrostlineRemote/FrostlineRemote.xcodeproj" -scheme FrostlineRemote \
      -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build | tail -20
    ;;
  alles)
    "$0" vorlagen
    "$0" assetstore
    "$0" einrichten
    "$0" spielbar
    "$0" tests
    "$0" build
    "$0" bilder
    "$0" messung
    ;;
  *)
    cat <<HILFE
Befehle:
  unity-installieren   Unity Hub (per Homebrew) und Unity $VERSION installieren
  vorlagen             freie Vorlagen (Poly Haven, CC0) laden: Texturen, Felsen, Schiffe, Laternen …
  assetstore           heruntergeladene Asset-Store-Pakete entpacken (Auswahl: tools/assetstore/auswahl.json)
  einrichten           Frostline › 1 · Projekt einrichten
  prototyp | insel     Szene bauen (2 × 2 km Prototyp oder ganze Insel)
  spielbar             Verlassene Insel spielbar machen (Atmosphäre reparieren, Drohne, Systeme, Kollision)
  blender              DJI-Drohnenmodell (.blend aus dem Download) mit Blender vom Stick in GLB wandeln
  pruefen              Verlassene Insel prüfen: Materialien, Terrain, Vergleichsbilder nach Ausgabe/pruefung
  tests                Unity-Tests (EditMode) laufen lassen
  build                Mac-Build nach Frostline/Builds/Frostline.app
  bilder               Screenshots von allen Checkpoints nach Ausgabe/bilder
  messung              60-s-fps-Messung, Ergebnis in Ausgabe/benchmark.json
  multiplayer          Multiplayer-Test: zwei Spiele, Host + Mitspieler über Unity Relay
  ios-pruefen          iPhone-App für den Simulator kompilieren (ohne Signieren)
  alles                vorlagen, assetstore, einrichten, spielbar, tests, build, bilder, messung
Qualitätsstufe für bilder/messung: QUALITY=High $0 messung (Standard: Medium)
HILFE
    ;;
esac
