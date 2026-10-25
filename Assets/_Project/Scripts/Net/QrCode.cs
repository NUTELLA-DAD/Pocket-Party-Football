/*
 * QR Code generator library - C# port (single file), shared by the Pocket Party games.
 * Ported from Project Nayuki's TypeScript version (qrcodegen.ts); numeric,
 * alphanumeric and byte modes only (no ECI, no kanji).
 *
 * Copyright (c) Project Nayuki. (MIT License)
 * https://www.nayuki.io/page/qr-code-generator-library
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of
 * this software and associated documentation files (the "Software"), to deal in
 * the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
 * the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 * - The above copyright notice and this permission notice shall be included in
 *   all copies or substantial portions of the Software.
 * - The Software is provided "as is", without warranty of any kind, express or
 *   implied, including but not limited to the warranties of merchantability,
 *   fitness for a particular purpose and noninfringement. In no event shall the
 *   authors or copyright holders be liable for any claim, damages or other
 *   liability, whether in an action of contract, tort or otherwise, arising from,
 *   out of or in connection with the Software or the use or other dealings in the
 *   Software.
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace PocketParty.Net
{
    public enum QrEcc { Low = 0, Medium = 1, Quartile = 2, High = 3 }

    public sealed class QrCode
    {
        public const int MinVersion = 1, MaxVersion = 40;
        const int PenaltyN1 = 3, PenaltyN2 = 3, PenaltyN3 = 40, PenaltyN4 = 10;
        static readonly int[] EccFormatBits = { 1, 0, 3, 2 };

        public readonly int Version, Size, Mask;
        public readonly QrEcc Ecc;
        readonly bool[,] modules;     // [y, x]
        bool[,] isFunction;

        public bool GetModule(int x, int y) => 0 <= x && x < Size && 0 <= y && y < Size && modules[y, x];

        public static QrCode EncodeText(string text, QrEcc ecl) => EncodeSegments(QrSegment.MakeSegments(text), ecl);

        public static QrCode EncodeSegments(IList<QrSegment> segs, QrEcc ecl, int minVersion = MinVersion,
            int maxVersion = MaxVersion, int mask = -1, bool boostEcl = true)
        {
            if (!(MinVersion <= minVersion && minVersion <= maxVersion && maxVersion <= MaxVersion) || mask < -1 || mask > 7)
                throw new ArgumentOutOfRangeException(nameof(minVersion), "Invalid value");
            int version, dataUsedBits;
            for (version = minVersion; ; version++)
            {
                int capacity = NumDataCodewords(version, ecl) * 8;
                int used = QrSegment.TotalBits(segs, version);
                if (used != -1 && used <= capacity) { dataUsedBits = used; break; }
                if (version >= maxVersion) throw new ArgumentException("Data too long");
            }
            foreach (var newEcl in new[] { QrEcc.Medium, QrEcc.Quartile, QrEcc.High })
                if (boostEcl && dataUsedBits <= NumDataCodewords(version, newEcl) * 8) ecl = newEcl;

            var bb = new List<bool>();
            foreach (var seg in segs)
            {
                AppendBits(seg.ModeBits, 4, bb);
                AppendBits(seg.NumChars, seg.CharCountBits(version), bb);
                bb.AddRange(seg.Data);
            }
            int dataCapacityBits = NumDataCodewords(version, ecl) * 8;
            AppendBits(0, Math.Min(4, dataCapacityBits - bb.Count), bb);
            AppendBits(0, (8 - bb.Count % 8) % 8, bb);
            for (int padByte = 0xEC; bb.Count < dataCapacityBits; padByte ^= 0xEC ^ 0x11) AppendBits(padByte, 8, bb);

            var dataCodewords = new byte[bb.Count / 8];
            for (int i = 0; i < bb.Count; i++) if (bb[i]) dataCodewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            return new QrCode(version, ecl, dataCodewords, mask);
        }

        public QrCode(int version, QrEcc ecl, byte[] dataCodewords, int msk)
        {
            if (version < MinVersion || version > MaxVersion) throw new ArgumentOutOfRangeException(nameof(version));
            if (msk < -1 || msk > 7) throw new ArgumentOutOfRangeException(nameof(msk));
            Version = version; Ecc = ecl; Size = version * 4 + 17;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(dataCodewords));
            if (msk == -1)
            {
                int minPenalty = int.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    ApplyMask(i); DrawFormatBits(i);
                    int penalty = PenaltyScore();
                    if (penalty < minPenalty) { msk = i; minPenalty = penalty; }
                    ApplyMask(i);
                }
            }
            Mask = msk;
            ApplyMask(msk);
            DrawFormatBits(msk);
            isFunction = null;
        }

        void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++) { SetFunction(6, i, i % 2 == 0); SetFunction(i, 6, i % 2 == 0); }
            DrawFinder(3, 3); DrawFinder(Size - 4, 3); DrawFinder(3, Size - 4);
            int[] pos = AlignmentPatternPositions();
            int n = pos.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)) DrawAlignment(pos[i], pos[j]);
            DrawFormatBits(0);
            DrawVersion();
        }

        void DrawFormatBits(int mask)
        {
            int data = EccFormatBits[(int)Ecc] << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) SetFunction(8, i, GetBit(bits, i));
            SetFunction(8, 7, GetBit(bits, 6));
            SetFunction(8, 8, GetBit(bits, 7));
            SetFunction(7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, GetBit(bits, i));
            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, GetBit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, GetBit(bits, i));
            SetFunction(8, Size - 8, true);
        }

        void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool color = GetBit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                SetFunction(a, b, color); SetFunction(b, a, color);
            }
        }

        void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (0 <= xx && xx < Size && 0 <= yy && yy < Size) SetFunction(xx, yy, dist != 2 && dist != 4);
                }
        }

        void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        void SetFunction(int x, int y, bool dark) { modules[y, x] = dark; isFunction[y, x] = true; }

        byte[] AddEccAndInterleave(byte[] data)
        {
            if (data.Length != NumDataCodewords(Version, Ecc)) throw new ArgumentException("Invalid argument");
            int numBlocks = NumErrorCorrectionBlocks[(int)Ecc, Version];
            int blockEccLen = EccCodewordsPerBlock[(int)Ecc, Version];
            int rawCodewords = NumRawDataModules(Version) / 8;
            int numShortBlocks = numBlocks - rawCodewords % numBlocks;
            int shortBlockLen = rawCodewords / numBlocks;

            var blocks = new List<byte[]>();
            byte[] rsDiv = ReedSolomonDivisor(blockEccLen);
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                var dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                byte[] ecc = ReedSolomonRemainder(dat, rsDiv);
                var block = new byte[shortBlockLen + 1];
                Array.Copy(dat, block, datLen);
                // short blocks get a padding byte at index datLen, skipped when interleaving
                Array.Copy(ecc, 0, block, block.Length - blockEccLen, blockEccLen);
                blocks.Add(block);
            }
            var result = new byte[rawCodewords];
            int r = 0;
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks) result[r++] = blocks[j][i];
            return result;
        }

        void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
            }
        }

        void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
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
                        case 7: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        default: throw new ArgumentOutOfRangeException(nameof(mask));
                    }
                    if (!isFunction[y, x] && invert) modules[y, x] = !modules[y, x];
                }
        }

        int PenaltyScore()
        {
            int result = 0;
            var history = new int[7];
            for (int pass = 0; pass < 2; pass++)       // 0 = rows, 1 = columns
                for (int a = 0; a < Size; a++)
                {
                    bool runColor = false; int run = 0;
                    Array.Clear(history, 0, 7);
                    for (int b = 0; b < Size; b++)
                    {
                        bool c = pass == 0 ? modules[a, b] : modules[b, a];
                        if (c == runColor)
                        {
                            run++;
                            if (run == 5) result += PenaltyN1;
                            else if (run > 5) result++;
                        }
                        else
                        {
                            AddHistory(run, history);
                            if (!runColor) result += CountPatterns(history) * PenaltyN3;
                            runColor = c; run = 1;
                        }
                    }
                    result += TerminateAndCount(runColor, run, history) * PenaltyN3;
                }
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = modules[y, x];
                    if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1]) result += PenaltyN2;
                }
            int dark = 0;
            foreach (bool c in modules) if (c) dark++;
            int total = Size * Size;
            int k = (int)Math.Ceiling(Math.Abs(dark * 20 - total * 10) / (double)total) - 1;
            return result + k * PenaltyN4;
        }

        int CountPatterns(int[] h)
        {
            int n = h[1];
            bool core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
            return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
        }

        int TerminateAndCount(bool runColor, int run, int[] h)
        {
            if (runColor) { AddHistory(run, h); run = 0; }
            run += Size;
            AddHistory(run, h);
            return CountPatterns(h);
        }

        void AddHistory(int run, int[] h)
        {
            if (h[0] == 0) run += Size;
            Array.Copy(h, 0, h, 1, 6);
            h[0] = run;
        }

        int[] AlignmentPatternPositions()
        {
            if (Version == 1) return new int[0];
            int numAlign = Version / 7 + 2;
            int step = (Version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
            var result = new int[numAlign];
            result[0] = 6;
            for (int i = numAlign - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        static int NumRawDataModules(int ver)
        {
            int result = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int numAlign = ver / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (ver >= 7) result -= 36;
            }
            return result;
        }

        static int NumDataCodewords(int ver, QrEcc ecl) =>
            NumRawDataModules(ver) / 8 - EccCodewordsPerBlock[(int)ecl, ver] * NumErrorCorrectionBlocks[(int)ecl, ver];

        static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = RsMul(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = RsMul((byte)root, 0x02);
            }
            return result;
        }

        static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < divisor.Length; i++) result[i] ^= RsMul(divisor[i], factor);
            }
            return result;
        }

        static byte RsMul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        internal static void AppendBits(int val, int len, List<bool> bb)
        {
            if (len < 0 || len > 31 || val >> len != 0) throw new ArgumentOutOfRangeException(nameof(val));
            for (int i = len - 1; i >= 0; i--) bb.Add(((val >> i) & 1) != 0);
        }

        static bool GetBit(int x, int i) => ((x >> i) & 1) != 0;

        static readonly int[,] EccCodewordsPerBlock = {
            {-1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},
            {-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28},
            {-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},
            {-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},
        };

        static readonly int[,] NumErrorCorrectionBlocks = {
            {-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25},
            {-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49},
            {-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8,  8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68},
            {-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81},
        };
    }

    public sealed class QrSegment
    {
        const string AlnumCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
        public readonly int ModeBits, NumChars;
        public readonly List<bool> Data;
        readonly int[] charCountBits;

        QrSegment(int modeBits, int[] ccBits, int numChars, List<bool> data)
        { ModeBits = modeBits; charCountBits = ccBits; NumChars = numChars; Data = data; }

        public int CharCountBits(int ver) => charCountBits[(ver + 7) / 17];

        public static QrSegment MakeBytes(byte[] data)
        {
            var bb = new List<bool>();
            foreach (byte b in data) QrCode.AppendBits(b, 8, bb);
            return new QrSegment(0x4, new[] { 8, 16, 16 }, data.Length, bb);
        }

        public static QrSegment MakeNumeric(string digits)
        {
            var bb = new List<bool>();
            for (int i = 0; i < digits.Length;)
            {
                int n = Math.Min(digits.Length - i, 3);
                QrCode.AppendBits(int.Parse(digits.Substring(i, n)), n * 3 + 1, bb);
                i += n;
            }
            return new QrSegment(0x1, new[] { 10, 12, 14 }, digits.Length, bb);
        }

        public static QrSegment MakeAlphanumeric(string text)
        {
            var bb = new List<bool>();
            int i;
            for (i = 0; i + 2 <= text.Length; i += 2)
                QrCode.AppendBits(AlnumCharset.IndexOf(text[i]) * 45 + AlnumCharset.IndexOf(text[i + 1]), 11, bb);
            if (i < text.Length) QrCode.AppendBits(AlnumCharset.IndexOf(text[i]), 6, bb);
            return new QrSegment(0x2, new[] { 9, 11, 13 }, text.Length, bb);
        }

        public static List<QrSegment> MakeSegments(string text)
        {
            var list = new List<QrSegment>();
            if (text.Length == 0) return list;
            if (IsNumeric(text)) list.Add(MakeNumeric(text));
            else if (IsAlphanumeric(text)) list.Add(MakeAlphanumeric(text));
            else list.Add(MakeBytes(Encoding.UTF8.GetBytes(text)));
            return list;
        }

        static bool IsNumeric(string s) { foreach (char c in s) if (c < '0' || c > '9') return false; return true; }
        static bool IsAlphanumeric(string s) { foreach (char c in s) if (AlnumCharset.IndexOf(c) < 0) return false; return true; }

        // -1 = a segment's length does not fit its count field at this version
        internal static int TotalBits(IList<QrSegment> segs, int version)
        {
            int result = 0;
            foreach (var seg in segs)
            {
                int cc = seg.CharCountBits(version);
                if (seg.NumChars >= (1 << cc)) return -1;
                result += 4 + cc + seg.Data.Count;
            }
            return result;
        }
    }
}
