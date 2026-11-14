#!/usr/bin/env python3
"""Frostline Relay: vermittelt die UDP-Pakete zwischen Spiel (Mac) und Handy über das Internet.

Beide Seiten senden nach außen an diesen Server; er merkt sich je Raum die Absenderadresse von
Spiel und Handy und reicht jedes Paket an die andere Seite weiter. So müssen Mac und Handy nicht
im selben WLAN sein, und an keinem Router muss ein Port geöffnet werden.

Rahmen (alle Zahlen in Bytes):
    0..3   "FLRY"
    4      Version (1)
    5      Rolle: 0 = Server-Antwort, 1 = Spiel, 2 = Handy
    6..21  Raum-ID = SHA-256("frostline-room-v1" + Schlüssel)[:16]
    22..29 Signatur = HMAC-SHA-256(Schlüssel, Bytes 0..21 + Nutzdaten)[:8]
    30..   Nutzdaten (das unveränderte Frostline-Funkpaket); leer = Lebenszeichen

Der Server kennt den Schlüssel nicht und prüft die Signatur nicht; das tun Spiel und Handy.
Auf ein Lebenszeichen antwortet er mit Rolle 0 und einem Byte: 1, wenn die Gegenseite da ist.

Start:  python3 frostline_relay.py --port 47821
"""
import argparse
import asyncio
import logging
import time

MAGIC = b"FLRY"
VERSION = 1
HEADER = 30
MAX_DATAGRAM = 1400
PEER_TIMEOUT = 15.0       # s ohne Paket: Seite gilt als weg
ROOM_TIMEOUT = 120.0      # s ohne Paket von beiden Seiten: Raum wird vergessen
MAX_ROOMS = 5000
RATE_PER_SECOND = 800     # Pakete je Absender (Handy sendet 250/s)


class Relay(asyncio.DatagramProtocol):
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.rooms = {}          # Raum-ID -> {Rolle: (Adresse, zuletzt gesehen)}
        self.buckets = {}        # Adresse -> (Guthaben, Zeitpunkt)
        self.transport = None
        self.forwarded = 0

    def connection_made(self, transport):
        self.transport = transport

    def allowed(self, addr, now):
        tokens, last = self.buckets.get(addr, (RATE_PER_SECOND, now))
        tokens = min(RATE_PER_SECOND, tokens + (now - last) * RATE_PER_SECOND)
        if tokens < 1:
            self.buckets[addr] = (tokens, now)
            return False
        self.buckets[addr] = (tokens - 1, now)
        return True

    def datagram_received(self, data, addr):
        if len(data) < HEADER or len(data) > MAX_DATAGRAM or data[:4] != MAGIC or data[4] != VERSION:
            return
        role = data[5]
        if role not in (1, 2):
            return
        now = self.clock()
        if not self.allowed(addr, now):
            return
        room = bytes(data[6:22])
        peers = self.rooms.get(room)
        if peers is None:
            if len(self.rooms) >= MAX_ROOMS:
                self.cleanup(now)
                if len(self.rooms) >= MAX_ROOMS:
                    return
            peers = self.rooms[room] = {}
        peers[role] = (addr, now)
        other = peers.get(3 - role)
        present = other is not None and now - other[1] < PEER_TIMEOUT

        if len(data) == HEADER:
            # Lebenszeichen: kurz antworten, ob die Gegenseite da ist.
            self.transport.sendto(MAGIC + bytes([VERSION, 0]) + room + bytes(8) + bytes([1 if present else 0]), addr)
            return
        if present:
            self.transport.sendto(data, other[0])
            self.forwarded += 1

    def cleanup(self, now):
        for room in [r for r, peers in self.rooms.items() if all(now - seen > ROOM_TIMEOUT for _, seen in peers.values())]:
            del self.rooms[room]
        for addr in [a for a, (_, seen) in self.buckets.items() if now - seen > ROOM_TIMEOUT]:
            del self.buckets[addr]


async def main():
    parser = argparse.ArgumentParser(description="Frostline Relay (UDP)")
    parser.add_argument("--host", default="0.0.0.0", help="Adresse, auf der gelauscht wird")
    parser.add_argument("--port", type=int, default=47821)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s")

    loop = asyncio.get_running_loop()
    transport, relay = await loop.create_datagram_endpoint(Relay, local_addr=(args.host, args.port))
    logging.info("Frostline Relay lauscht auf UDP %s:%d", args.host, args.port)
    try:
        while True:
            await asyncio.sleep(30)
            relay.cleanup(time.monotonic())
            logging.info("Räume: %d, weitergeleitet: %d", len(relay.rooms), relay.forwarded)
    finally:
        transport.close()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
