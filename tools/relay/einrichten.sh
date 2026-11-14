#!/bin/bash
# Frostline Relay auf einem Linux-Server mit systemd einrichten (braucht dort python3 und sudo/root).
# Aufruf:  tools/relay/einrichten.sh <ssh-ziel>      z. B.  tools/relay/einrichten.sh root@203.0.113.5
# Danach im Spiel: F1 › Spiel › „Übers Internet“ › Server eintragen und QR-Code scannen.
set -euo pipefail
ZIEL="${1:?SSH-Ziel fehlt, z. B. root@203.0.113.5}"
DIR="$(cd "$(dirname "$0")" && pwd)"

scp "$DIR/frostline_relay.py" "$DIR/frostline-relay.service" "$ZIEL:/tmp/"
ssh "$ZIEL" 'set -e
  SUDO=""; [ "$(id -u)" = 0 ] || SUDO=sudo
  command -v python3 >/dev/null || { echo "python3 fehlt auf dem Server" >&2; exit 1; }
  $SUDO install -D -m 644 /tmp/frostline_relay.py /opt/frostline-relay/frostline_relay.py
  $SUDO install -m 644 /tmp/frostline-relay.service /etc/systemd/system/frostline-relay.service
  rm -f /tmp/frostline_relay.py /tmp/frostline-relay.service
  $SUDO systemctl daemon-reload
  $SUDO systemctl enable --now frostline-relay
  $SUDO systemctl restart frostline-relay
  if command -v ufw >/dev/null && $SUDO ufw status | grep -q "Status: active"; then $SUDO ufw allow 47821/udp; fi
  systemctl is-active frostline-relay'
echo "✓ Frostline Relay läuft. Prüfen:  python3 $DIR/pruefen.py ${ZIEL#*@}"
