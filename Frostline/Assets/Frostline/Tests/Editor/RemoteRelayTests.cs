using System;
using System.IO;
using System.Text;
using Frostline.Remote;
using NUnit.Framework;
using UnityEngine;

namespace Frostline.Tests
{
    public class RemoteRelayTests
    {
        static byte[] TestSecret()
        {
            var s = new byte[32];
            for (int i = 0; i < 32; i++) s[i] = (byte)i;
            return s;
        }

        static string Hex(byte[] data, int length)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < length; i++) sb.Append(data[i].ToString("x2"));
            return sb.ToString();
        }

        [Test]
        public void Frame_MatchesSharedTestVector()
        {
            // Derselbe Vektor steht im Python-Relay und in den Swift-Tests.
            byte[] secret = TestSecret();
            var output = new byte[64];
            int length = RemoteRelay.Seal(output, RemoteRelay.RolePhone, RemoteRelay.RoomId(secret), secret, new byte[] { 1, 2, 3 }, 3);
            Assert.AreEqual("464c525901025ff9a001b4697e8eb69dea7d3cda081a869d8f12c1aacd6a010203", Hex(output, length));
        }

        [Test]
        public void Open_AcceptsOwnFrameAndRejectsTampering()
        {
            byte[] secret = TestSecret(), room = RemoteRelay.RoomId(secret);
            var frame = new byte[64];
            int length = RemoteRelay.Seal(frame, RemoteRelay.RolePhone, room, secret, new byte[] { 9, 8, 7, 6 }, 4);
            int offset, payload;
            bool present;
            Assert.AreEqual(RemoteRelay.Kind.Peer, RemoteRelay.Open(frame, length, room, secret, RemoteRelay.RolePhone, out offset, out payload, out present));
            Assert.AreEqual(4, payload);
            Assert.AreEqual(9, frame[offset]);

            frame[offset] ^= 1; // ein Bit verfälscht
            Assert.AreEqual(RemoteRelay.Kind.Invalid, RemoteRelay.Open(frame, length, room, secret, RemoteRelay.RolePhone, out offset, out payload, out present));

            byte[] other = new byte[32];
            Assert.AreEqual(RemoteRelay.Kind.Invalid, RemoteRelay.Open(frame, length, RemoteRelay.RoomId(other), other, RemoteRelay.RolePhone, out offset, out payload, out present));
        }

        [Test]
        public void ServerAck_ReportsPeer()
        {
            byte[] secret = TestSecret(), room = RemoteRelay.RoomId(secret);
            var ack = new byte[31];
            Encoding.ASCII.GetBytes("FLRY").CopyTo(ack, 0);
            ack[4] = 1;
            ack[5] = RemoteRelay.RoleServer;
            Array.Copy(room, 0, ack, 6, 16);
            ack[30] = 1;
            int offset, payload;
            bool present;
            Assert.AreEqual(RemoteRelay.Kind.ServerAck, RemoteRelay.Open(ack, ack.Length, room, secret, RemoteRelay.RolePhone, out offset, out payload, out present));
            Assert.IsTrue(present);
        }

        [Test]
        public void PairingUrl_RoundTrips()
        {
            byte[] secret = TestSecret();
            string url = RemoteRelay.PairingUrl("relay.example.org", 47821, secret, "Davids Mac");
            StringAssert.StartsWith("frostline://pair?v=1&h=relay.example.org&p=47821&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8", url);
            string host, name;
            int port;
            byte[] parsed;
            Assert.IsTrue(RemoteRelay.TryParsePairing(url, out host, out port, out parsed, out name));
            Assert.AreEqual("relay.example.org", host);
            Assert.AreEqual(47821, port);
            Assert.AreEqual("Davids Mac", name);
            CollectionAssert.AreEqual(secret, parsed);
            Assert.IsFalse(RemoteRelay.TryParsePairing("https://example.org", out host, out port, out parsed, out name));
        }

        [Test]
        public void QrCode_HasFinderPatternsAndPicksVersion()
        {
            QrCode small = QrCode.Encode("HELLO");
            Assert.AreEqual(1, small.Version);
            Assert.AreEqual(21, small.Size);
            // Suchmuster: dunkle Ecke, heller Ring, dunkler Kern.
            Assert.IsTrue(small[0, 0]);
            Assert.IsFalse(small[1, 1]);
            Assert.IsTrue(small[3, 3]);
            Assert.IsTrue(small[small.Size - 1, 0]);
            Assert.IsTrue(small[0, small.Size - 1]);

            string url = RemoteRelay.PairingUrl("31.70.130.250", 47821, TestSecret(), "MacBook Air von David");
            QrCode pairing = QrCode.Encode(url);
            Assert.GreaterOrEqual(pairing.Version, 6);
            Assert.LessOrEqual(pairing.Version, 8);

            // Für die Gegenprobe mit einem echten QR-Leser (CoreImage) als PNG ablegen.
            WritePng(pairing, Path.Combine(Path.GetTempPath(), "frostline_qr_test.png"));
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "frostline_qr_test.txt"), url);
            WritePng(small, Path.Combine(Path.GetTempPath(), "frostline_qr_hello.png"));
        }

        static void WritePng(QrCode qr, string path)
        {
            const int scale = 8, quiet = 4;
            int size = (qr.Size + quiet * 2) * scale;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int mx = x / scale - quiet, my = (size - 1 - y) / scale - quiet;
                    bool dark = qr[mx, my];
                    pixels[y * size + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
