using System;
using System.Security.Cryptography;
using System.Text;

namespace Frostline.Remote
{
    /// <summary>
    /// Fernsteuerung übers Internet: Spiel und Handy senden beide an den Frostline Relay
    /// (tools/relay/frostline_relay.py), der die Pakete weiterreicht. Jedes Funkpaket steckt in einem
    /// Rahmen mit Raum-ID und Signatur:
    ///   0..3 „FLRY“, 4 Version, 5 Rolle (0 Server, 1 Spiel, 2 Handy), 6..21 Raum-ID,
    ///   22..29 HMAC-SHA-256(Schlüssel, Bytes 0..21 + Nutzdaten)[:8], 30.. Nutzdaten.
    /// Den 256-Bit-Schlüssel kennen nur Spiel und Handy (über den QR-Code); der Server sieht ihn nie.
    /// Dieselbe Definition steht in Swift (FrostlineRemoteKit/RelayFrame.swift) und Python; alle
    /// drei prüfen gegen denselben Testvektor.
    /// </summary>
    public static class RemoteRelay
    {
        public const int DefaultPort = 47821;
        public const int HeaderSize = 30;
        public const int TagSize = 8;
        public const int SecretSize = 32;
        public const byte Version = 1;
        public const byte RoleServer = 0;
        public const byte RoleGame = 1;
        public const byte RolePhone = 2;
        public const string Scheme = "frostline";

        static readonly byte[] RoomPrefix = Encoding.ASCII.GetBytes("frostline-room-v1");

        public enum Kind { Invalid, Peer, ServerAck }

        /// <summary>Raum-ID: die ersten 16 Bytes von SHA-256("frostline-room-v1" + Schlüssel).</summary>
        public static byte[] RoomId(byte[] secret)
        {
            var input = new byte[RoomPrefix.Length + secret.Length];
            Buffer.BlockCopy(RoomPrefix, 0, input, 0, RoomPrefix.Length);
            Buffer.BlockCopy(secret, 0, input, RoomPrefix.Length, secret.Length);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(input);
                var room = new byte[16];
                Buffer.BlockCopy(hash, 0, room, 0, 16);
                return room;
            }
        }

        public static byte[] NewSecret()
        {
            var secret = new byte[SecretSize];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
            return secret;
        }

        public static bool IsFrame(byte[] data, int length)
        {
            return length >= HeaderSize && data[0] == (byte)'F' && data[1] == (byte)'L' && data[2] == (byte)'R' && data[3] == (byte)'Y' && data[4] == Version;
        }

        /// <summary>Packt ein Funkpaket (oder nichts = Lebenszeichen) in einen signierten Rahmen.</summary>
        public static int Seal(byte[] output, byte role, byte[] room, byte[] secret, byte[] payload, int payloadLength)
        {
            if (output.Length < HeaderSize + payloadLength) throw new ArgumentException("Puffer zu klein");
            output[0] = (byte)'F'; output[1] = (byte)'L'; output[2] = (byte)'R'; output[3] = (byte)'Y';
            output[4] = Version;
            output[5] = role;
            Buffer.BlockCopy(room, 0, output, 6, 16);
            if (payloadLength > 0) Buffer.BlockCopy(payload, 0, output, HeaderSize, payloadLength);
            byte[] tag = Tag(secret, output, payloadLength);
            Buffer.BlockCopy(tag, 0, output, 22, TagSize);
            return HeaderSize + payloadLength;
        }

        /// <summary>
        /// Prüft einen empfangenen Rahmen. Peer: Rolle, Raum und Signatur stimmen; die Nutzdaten liegen ab
        /// payloadOffset. ServerAck: Antwort des Servers auf ein Lebenszeichen (peerPresent).
        /// </summary>
        public static Kind Open(byte[] data, int length, byte[] room, byte[] secret, byte expectedRole,
                                out int payloadOffset, out int payloadLength, out bool peerPresent)
        {
            payloadOffset = HeaderSize;
            payloadLength = 0;
            peerPresent = false;
            if (!IsFrame(data, length) || !SameRoom(data, room)) return Kind.Invalid;
            if (data[5] == RoleServer)
            {
                peerPresent = length > HeaderSize && data[HeaderSize] == 1;
                return Kind.ServerAck;
            }
            if (data[5] != expectedRole) return Kind.Invalid;
            payloadLength = length - HeaderSize;
            byte[] tag = Tag(secret, data, payloadLength);
            int diff = 0;
            for (int i = 0; i < TagSize; i++) diff |= tag[i] ^ data[22 + i];
            return diff == 0 ? Kind.Peer : Kind.Invalid;
        }

        static bool SameRoom(byte[] data, byte[] room)
        {
            for (int i = 0; i < 16; i++)
                if (data[6 + i] != room[i]) return false;
            return true;
        }

        static byte[] Tag(byte[] secret, byte[] frame, int payloadLength)
        {
            var input = new byte[22 + payloadLength];
            Buffer.BlockCopy(frame, 0, input, 0, 22);
            if (payloadLength > 0) Buffer.BlockCopy(frame, HeaderSize, input, 22, payloadLength);
            using (var hmac = new HMACSHA256(secret)) return hmac.ComputeHash(input);
        }

        // ------------------------------------------------------------ Kopplung per QR-Code

        /// <summary>frostline://pair?v=1&amp;h=Server&amp;p=Port&amp;k=Schlüssel&amp;n=Name</summary>
        public static string PairingUrl(string host, int port, byte[] secret, string name)
        {
            return Scheme + "://pair?v=1&h=" + Uri.EscapeDataString(host) + "&p=" + port
                + "&k=" + Base64Url(secret) + "&n=" + Uri.EscapeDataString(name ?? "");
        }

        public static bool TryParsePairing(string url, out string host, out int port, out byte[] secret, out string name)
        {
            host = null; port = 0; secret = null; name = "";
            if (string.IsNullOrEmpty(url) || !url.StartsWith(Scheme + "://pair?", StringComparison.OrdinalIgnoreCase)) return false;
            string query = url.Substring(url.IndexOf('?') + 1);
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq), value = Uri.UnescapeDataString(part.Substring(eq + 1));
                switch (key)
                {
                    case "h": host = value; break;
                    case "p": int.TryParse(value, out port); break;
                    case "k": secret = FromBase64Url(value); break;
                    case "n": name = value; break;
                }
            }
            return !string.IsNullOrEmpty(host) && port > 0 && port < 65536 && secret != null && secret.Length == SecretSize;
        }

        public static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] FromBase64Url(string text)
        {
            try
            {
                string s = text.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                }
                return Convert.FromBase64String(s);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
