#!/bin/bash
# Frostline auf dem Mac per Terminal: einrichten, Insel bauen, Mac-Build, Tests, Messung.
# Aufruf aus dem Repository-Ordner, z. B.:  tools/mac/frostline.sh alles
set -euo pipefail

VERSION="6000.6.9f1"
CHANGESET="8c45277fb9c5"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$ROOT/Frostline"
OUT="$ROOT/Ausgabe"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
HUB="/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"
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
    if [ ! -d "/Applications/Unity Hub.app" ]; then
      command -v brew >/dev/null || { echo "Homebrew fehlt: https://brew.sh" >&2; exit 1; }
      brew install --cask unity-hub
    fi
    "$HUB" -- --headless install --version "$VERSION" --changeset "$CHANGESET" --architecture arm64
    echo "Danach einmal Unity Hub öffnen, mit dem Unity-Konto anmelden und die Personal-Lizenz aktivieren."
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
  ios-pruefen)
    xcodebuild -project "$ROOT/FrostlineRemote/FrostlineRemote.xcodeproj" -scheme FrostlineRemote \
      -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build | tail -20
    ;;
  alles)
    "$0" einrichten
    "$0" insel
    "$0" tests
    "$0" build
    "$0" bilder
    "$0" messung
    ;;
  *)
    cat <<HILFE
Befehle:
  unity-installieren   Unity Hub (per Homebrew) und Unity $VERSION installieren
  einrichten           Frostline › 1 · Projekt einrichten
  prototyp | insel     Szene bauen (2 × 2 km Prototyp oder ganze Insel)
  tests                Unity-Tests (EditMode) laufen lassen
  build                Mac-Build nach Frostline/Builds/Frostline.app
  bilder               Screenshots von allen Checkpoints nach Ausgabe/bilder
  messung              60-s-fps-Messung, Ergebnis in Ausgabe/benchmark.json
  ios-pruefen          iPhone-App für den Simulator kompilieren (ohne Signieren)
  alles                einrichten, insel, tests, build, bilder, messung
Qualitätsstufe für bilder/messung: QUALITY=High $0 messung (Standard: Medium)
HILFE
    ;;
esac
