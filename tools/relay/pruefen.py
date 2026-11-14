#!/usr/bin/env python3
"""Prüft, ob ein Frostline Relay antwortet:  python3 tools/relay/pruefen.py <host> [port]

Schickt ein Lebenszeichen mit einem zufälligen Test-Raum und wartet auf die Antwort des Servers.
Antwortet er nicht, ist meist der UDP-Port in der Firewall (Server oder Hoster) noch zu.
"""
import os
import socket
import sys
import time

host = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
port = int(sys.argv[2]) if len(sys.argv) > 2 else 47821
room = os.urandom(16)
frame = b"FLRY" + bytes([1, 2]) + room + bytes(8)

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.settimeout(1.0)
for attempt in range(3):
    start = time.monotonic()
    sock.sendto(frame, (host, port))
    try:
        data, _ = sock.recvfrom(256)
    except socket.timeout:
        continue
    if data[:4] == b"FLRY" and data[5] == 0 and data[6:22] == room:
        print(f"✓ Frostline Relay {host}:{port} antwortet ({(time.monotonic() - start) * 1000:.0f} ms)")
        sys.exit(0)
print(f"✗ Keine Antwort von {host}:{port} (UDP). Läuft der Dienst, und ist der Port in der Firewall offen?")
sys.exit(1)
