"""Tests für den Frostline Relay: python3 tools/relay/test_frostline_relay.py"""
import hashlib
import hmac
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import frostline_relay as relay  # noqa: E402

SECRET = bytes(range(32))


def frame(role, payload=b"", secret=SECRET):
    room = hashlib.sha256(b"frostline-room-v1" + secret).digest()[:16]
    header = b"FLRY" + bytes([1, role]) + room
    tag = hmac.new(secret, header + payload, hashlib.sha256).digest()[:8]
    return header + tag + payload


class FakeTransport:
    def __init__(self):
        self.sent = []

    def sendto(self, data, addr):
        self.sent.append((data, addr))


class Clock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


class RelayTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.relay = relay.Relay(clock=self.clock)
        self.transport = FakeTransport()
        self.relay.connection_made(self.transport)

    def test_vector_matches_game_and_app(self):
        # Derselbe Prüfvektor steht in den C#- und Swift-Tests.
        self.assertEqual(frame(2, bytes([1, 2, 3])).hex(),
                         "464c525901025ff9a001b4697e8eb69dea7d3cda081a869d8f12c1aacd6a010203")

    def test_forwards_between_game_and_phone(self):
        game, phone = ("1.1.1.1", 1000), ("2.2.2.2", 2000)
        self.relay.datagram_received(frame(1), game)           # Spiel meldet sich
        self.relay.datagram_received(frame(2, b"stick"), phone)  # Handy schickt Sticks
        self.assertIn((frame(2, b"stick"), game), self.transport.sent)
        self.relay.datagram_received(frame(1, b"telemetrie"), game)
        self.assertIn((frame(1, b"telemetrie"), phone), self.transport.sent)

    def test_keepalive_reports_peer(self):
        phone = ("2.2.2.2", 2000)
        self.relay.datagram_received(frame(2), phone)
        data, addr = self.transport.sent[-1]
        self.assertEqual(addr, phone)
        self.assertEqual(data[5], 0)
        self.assertEqual(data[-1], 0, "Spiel noch nicht da")
        self.relay.datagram_received(frame(1), ("1.1.1.1", 1000))
        self.relay.datagram_received(frame(2), phone)
        self.assertEqual(self.transport.sent[-1][0][-1], 1, "Spiel jetzt da")

    def test_other_rooms_are_separate(self):
        game = ("1.1.1.1", 1000)
        self.relay.datagram_received(frame(1), game)
        stranger = frame(2, b"fremd", secret=bytes(32))
        self.relay.datagram_received(stranger, ("3.3.3.3", 3000))
        self.assertNotIn((stranger, game), self.transport.sent)

    def test_stale_peer_is_not_used(self):
        game, phone = ("1.1.1.1", 1000), ("2.2.2.2", 2000)
        self.relay.datagram_received(frame(1), game)
        self.clock.now += relay.PEER_TIMEOUT + 1
        self.relay.datagram_received(frame(2, b"stick"), phone)
        self.assertNotIn((frame(2, b"stick"), game), self.transport.sent)

    def test_rejects_garbage_and_rate_limits(self):
        self.relay.datagram_received(b"GET / HTTP/1.1\r\n", ("4.4.4.4", 1))
        self.assertEqual(self.transport.sent, [])
        sender = ("5.5.5.5", 5)
        for _ in range(relay.RATE_PER_SECOND + 50):
            self.relay.datagram_received(frame(2), sender)
        self.assertLessEqual(len(self.transport.sent), relay.RATE_PER_SECOND)

    def test_cleanup_forgets_idle_rooms(self):
        self.relay.datagram_received(frame(1), ("1.1.1.1", 1000))
        self.clock.now += relay.ROOM_TIMEOUT + 1
        self.relay.cleanup(self.clock.now)
        self.assertEqual(self.relay.rooms, {})


if __name__ == "__main__":
    unittest.main()
