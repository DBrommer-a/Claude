using System;
using System.Collections.Generic;
using System.Text;

namespace Frostline.Remote
{
    /// <summary>
    /// QR-Code-Erzeuger nach ISO/IEC 18004 für Texte im Byte-Modus (UTF-8), Fehlerkorrektur M
    /// (etwa 15 % dürfen fehlen), Version 1–40 je nach Länge. Ohne Unity, damit testbar.
    /// Ergebnis: <see cref="Size"/> × <see cref="Size"/> Module, true = dunkel.
    /// </summary>
    public sealed class QrCode
    {
        // Tabellen für Stufe M (Index = Version): Fehlerkorrektur-Bytes je Block und Anzahl Blöcke.
        static readonly int[] EccPerBlockM =
        {
            -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
            26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
        };
        static readonly int[] BlocksM =
        {
            -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
            17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49,
        };
        const int FormatBitsM = 0; // M = 00

        public int Version { get; private set; }
        public int Size { get; private set; }
        public int Mask { get; private set; }

        bool[,] modules;      // [y, x]
        bool[,] isFunction;

        public bool this[int x, int y] { get { return x >= 0 && y >= 0 && x < Size && y < Size && modules[y, x]; } }

        public static QrCode Encode(string text)
        {
            return Encode(Encoding.UTF8.GetBytes(text));
        }

        public static QrCode Encode(byte[] data)
        {
            for (int version = 1; version <= 40; version++)
            {
                int capacityBits = DataCodewords(version) * 8;
                int countBits = version <= 9 ? 8 : 16;
                int needed = 4 + countBits + data.Length * 8;
                if (needed <= capacityBits) return new QrCode(version, data, countBits, capacityBits);
            }
            throw new ArgumentException("Text zu lang für einen QR-Code");
        }

        QrCode(int version, byte[] data, int countBits, int capacityBits)
        {
            Version = version;
            Size = version * 4 + 17;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];

            // Bitstrom: Byte-Modus, Länge, Daten, Abschluss, Füllbytes.
            var bits = new List<bool>();
            Append(bits, 0x4, 4);
            Append(bits, data.Length, countBits);
            foreach (byte b in data) Append(bits, b, 8);
            Append(bits, 0, Math.Min(4, capacityBits - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(codewords));

            // Maske mit der geringsten Strafpunktzahl wählen.
            int best = 0, bestPenalty = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int penalty = Penalty();
                if (penalty < bestPenalty) { best = m; bestPenalty = penalty; }
                ApplyMask(m); // XOR: zweimal = rückgängig
            }
            Mask = best;
            ApplyMask(best);
            DrawFormatBits(best);
            isFunction = null;
        }

        static void Append(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        // ------------------------------------------------------------ Kapazität

        static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int align = version / 7 + 2;
                result -= (25 * align - 10) * align - 55;
                if (version >= 7) result -= 36;
            }
            return result;
        }

        static int DataCodewords(int version)
        {
            return RawDataModules(version) / 8 - EccPerBlockM[version] * BlocksM[version];
        }

        // ------------------------------------------------------------ Fehlerkorrektur (Reed-Solomon)

        byte[] AddEccAndInterleave(byte[] data)
        {
            int blocks = BlocksM[Version];
            int eccLength = EccPerBlockM[Version];
            int raw = RawDataModules(Version) / 8;
            int shortBlocks = blocks - raw % blocks;
            int shortLength = raw / blocks;

            // Alle Blöcke gleich lang (shortLength + 1); die Korrektur steht hinten, kurze Blöcke haben
            // vor ihr ein Platzhalter-Byte.
            byte[] divisor = ReedSolomonDivisor(eccLength);
            var all = new List<byte[]>();
            for (int i = 0, k = 0; i < blocks; i++)
            {
                int dataLength = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
                var block = new byte[shortLength + 1];
                Array.Copy(data, k, block, 0, dataLength);
                byte[] ecc = ReedSolomonRemainder(data, k, dataLength, divisor);
                k += dataLength;
                Array.Copy(ecc, 0, block, block.Length - eccLength, eccLength);
                all.Add(block);
            }

            var result = new byte[raw];
            for (int i = 0, n = 0; i < shortLength + 1; i++)
                for (int j = 0; j < all.Count; j++)
                    if (i != shortLength - eccLength || j >= shortBlocks) result[n++] = all[j][i];
            return result;
        }

        static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] ReedSolomonRemainder(byte[] data, int offset, int length, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            for (int i = 0; i < length; i++)
            {
                int factor = data[offset + i] ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int j = 0; j < result.Length; j++) result[j] ^= Multiply(divisor[j], factor);
            }
            return result;
        }

        static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        // ------------------------------------------------------------ Muster

        void SetFunction(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            int[] align = AlignmentPositions();
            int last = align.Length - 1;
            for (int i = 0; i < align.Length; i++)
                for (int j = 0; j < align.Length; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == last || i == last && j == 0)) DrawAlignment(align[i], align[j]);

            DrawFormatBits(0); // Platz reservieren; echte Werte nach der Maskenwahl
            DrawVersion();
        }

        void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunction(xx, yy, dist != 2 && dist != 4);
                }
        }

        void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int count = Version / 7 + 2;
            int step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        void DrawFormatBits(int mask)
        {
            int data = FormatBitsM << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));

            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
            SetFunction(8, Size - 8, true); // immer dunkel
        }

        void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        static bool Bit(int value, int i) { return ((value >> i) & 1) != 0; }

        void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
                }
            }
        }

        void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (isFunction[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) modules[y, x] = !modules[y, x];
                }
        }

        /// <summary>Strafpunkte nach Regel 1 (lange Reihen), 2 (2×2-Blöcke) und 4 (Hell-Dunkel-Verhältnis).</summary>
        int Penalty()
        {
            int penalty = 0, dark = 0;
            for (int y = 0; y < Size; y++)
            {
                int runX = 1, runY = 1;
                for (int x = 0; x < Size; x++)
                {
                    if (modules[y, x]) dark++;
                    if (x > 0)
                    {
                        if (modules[y, x] == modules[y, x - 1]) runX++;
                        else { if (runX >= 5) penalty += runX - 2; runX = 1; }
                        if (modules[x, y] == modules[x - 1, y]) runY++;
                        else { if (runY >= 5) penalty += runY - 2; runY = 1; }
                    }
                    if (x > 0 && y > 0)
                    {
                        bool c = modules[y, x];
                        if (c == modules[y, x - 1] && c == modules[y - 1, x] && c == modules[y - 1, x - 1]) penalty += 3;
                    }
                }
                if (runX >= 5) penalty += runX - 2;
                if (runY >= 5) penalty += runY - 2;
            }
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return penalty + Math.Max(0, k) * 10;
        }
    }
}
