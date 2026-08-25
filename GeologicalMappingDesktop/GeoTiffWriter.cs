using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace GeologicalMappingDesktop
{
    /// <summary>
    /// 写出带内嵌空间参考与地理变换的 GeoTIFF（LZW 压缩，经典 TIFF II 小端）。
    /// 地理变换取自配准仿射参数：无旋转时写 ModelPixelScale+ModelTiepoint，
    /// 有旋转/斜切时写 ModelTransformation（均以像素角点坐标为原点）。
    /// 坐标系用 EPSG 码写入 GeoKeyDirectory（ProjectedCSTypeGeoKey / GeographicTypeGeoKey）。
    /// </summary>
    public static class GeoTiffWriter
    {
        private const double Eps = 1e-9;

        public static void Save(Bitmap bmp, string outputPath, GeoreferenceHelper.AffineParams affine, int wkid, bool isGeographic)
        {
            if (bmp == null) throw new ArgumentNullException(nameof(bmp));
            if (affine == null) throw new ArgumentNullException(nameof(affine));
            if (wkid <= 0) throw new ArgumentException("wkid 必须大于 0", nameof(wkid));

            int width = bmp.Width;
            int height = bmp.Height;

            // 像素角点坐标（世界文件以像素中心为原点，GeoTIFF 以像素角点为原点）
            double cornerX = affine.C - 0.5 * affine.A - 0.5 * affine.B;
            double cornerY = affine.F - 0.5 * affine.D - 0.5 * affine.E;

            bool axisAligned = Math.Abs(affine.B) < Eps && Math.Abs(affine.D) < Eps;

            // LZW 压缩像素（BGR -> RGB 顺序）
            byte[] lzw;
            {
                byte[] rgb = ToRgbBytes(bmp);
                lzw = LzwEncode(rgb);
            }

            var mem = new MemoryStream();
            var w = new BinaryWriter(mem);

            // 头：II, 42, IFD 偏移
            w.Write((byte)'I'); w.Write((byte)'I');
            w.Write((ushort)42);
            w.Write((uint)8);

            // 构建 IFD 条目
            var entries = new List<IfdEntry>();

            entries.Add(new IfdEntry(256, TypeLong, 1, U32((uint)width)));
            entries.Add(new IfdEntry(257, TypeLong, 1, U32((uint)height)));
            entries.Add(new IfdEntry(258, TypeShort, 3, null)); // BitsPerSample -> 数据区
            entries.Add(new IfdEntry(259, TypeShort, 1, U16(5))); // LZW
            entries.Add(new IfdEntry(262, TypeShort, 1, U16(2))); // RGB
            entries.Add(new IfdEntry(273, TypeLong, 1, null)); // StripOffsets -> 像素数据偏移
            entries.Add(new IfdEntry(277, TypeShort, 1, U16(3)));
            entries.Add(new IfdEntry(278, TypeLong, 1, U32((uint)height))); // RowsPerStrip
            entries.Add(new IfdEntry(279, TypeLong, 1, U32((uint)lzw.Length))); // StripByteCounts
            entries.Add(new IfdEntry(282, TypeRational, 1, null)); // XResolution -> 数据区
            entries.Add(new IfdEntry(283, TypeRational, 1, null)); // YResolution -> 数据区
            entries.Add(new IfdEntry(284, TypeShort, 1, U16(1))); // PlanarConfiguration
            entries.Add(new IfdEntry(296, TypeShort, 1, U16(1))); // ResolutionUnit = none

            if (axisAligned)
            {
                entries.Add(new IfdEntry(33550, TypeDouble, 3, null)); // ModelPixelScale
                entries.Add(new IfdEntry(33922, TypeDouble, 6, null)); // ModelTiepoint
            }
            else
            {
                entries.Add(new IfdEntry(34264, TypeDouble, 16, null)); // ModelTransformation
            }

            ushort[] geokeys = BuildGeoKeys(isGeographic, wkid);
            entries.Add(new IfdEntry(34735, TypeShort, (uint)geokeys.Length, null)); // GeoKeyDirectory

            entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

            // 数据区布局：IFD 之后依次存放超出 4 字节的条目值
            uint ifdSize = (uint)(2 + entries.Count * 12 + 4);
            uint dataPos = 8 + ifdSize;
            foreach (var e in entries)
            {
                byte[] payload = PayloadData(e, width, height, lzw, affine, cornerX, cornerY, axisAligned, geokeys);
                if (payload != null)
                {
                    e.DataOffset = dataPos;
                    dataPos += (uint)payload.Length;
                }
            }
            uint pixelOffset = dataPos;
            var stripOffsetsEntry = entries.Find(e => e.Tag == 273);
            if (stripOffsetsEntry != null)
                stripOffsetsEntry.DataOffset = pixelOffset;

            // 写 IFD
            w.Write((ushort)entries.Count);
            foreach (var e in entries)
            {
                w.Write(e.Tag);
                w.Write(e.Type);
                w.Write(e.Count);
                if (e.InlineValue != null)
                {
                    w.Write(e.InlineValue);
                    int pad = 4 - e.InlineValue.Length;
                    for (int i = 0; i < pad; i++) w.Write((byte)0);
                }
                else
                {
                    w.Write(e.DataOffset);
                }
            }
            w.Write((uint)0); // 下一个 IFD

            // 写数据区
            foreach (var e in entries)
            {
                byte[] payload = PayloadData(e, width, height, lzw, affine, cornerX, cornerY, axisAligned, geokeys);
                if (payload != null)
                    w.Write(payload);
            }
            // 写像素数据（StripOffsets 指向这里）
            w.Write(lzw);

            w.Flush();
            File.WriteAllBytes(outputPath, mem.ToArray());
        }

        private static byte[] ToRgbBytes(Bitmap bmp)
        {
            int width = bmp.Width, height = bmp.Height;
            var rect = new Rectangle(0, 0, width, height);
            var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = bd.Stride;
                int rowBytes = Math.Abs(stride);
                byte[] raw = new byte[rowBytes * height];
                Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
                byte[] rgb = new byte[width * height * 3];
                for (int y = 0; y < height; y++)
                {
                    int rowStart = stride >= 0 ? y * stride : (height - 1 - y) * stride;
                    int outRow = y * width * 3;
                    for (int x = 0; x < width; x++)
                    {
                        int o = rowStart + x * 3;
                        int oo = outRow + x * 3;
                        rgb[oo] = raw[o + 2];     // R
                        rgb[oo + 1] = raw[o + 1]; // G
                        rgb[oo + 2] = raw[o];     // B
                    }
                }
                return rgb;
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        /// <summary>TIFF LZW 编码（与 libtiff 位宽/字典重置时序一致）</summary>
        private static byte[] LzwEncode(byte[] data)
        {
            const int ClearCode = 256;
            const int EoiCode = 257;
            const int MaxFree = 4094; // 0..4093 可用码，4094 达到上限

            var dict = new Dictionary<int, int>();
            int nextFree;
            int codeWidth;
            int maxCode;

            void ResetDict()
            {
                dict.Clear();
                nextFree = 258;
                codeWidth = 9;
                maxCode = 511; // (1<<9)-1
            }
            ResetDict();

            var bitBuf = 0;
            var bitCnt = 0;
            var outBuf = new MemoryStream();
            var outList = new List<byte>();

            void FlushBits()
            {
                while (bitCnt >= 8)
                {
                    bitCnt -= 8;
                    outList.Add((byte)(bitBuf >> bitCnt));
                }
                bitBuf &= (bitCnt == 0) ? 0 : ((1 << bitCnt) - 1);
            }

            void WriteBits(int code)
            {
                bitBuf = (bitBuf << codeWidth) | code;
                bitCnt += codeWidth;
                FlushBits();
            }

            WriteBits(ClearCode);

            int prefix = -1;
            for (int i = 0; i < data.Length; i++)
            {
                int b = data[i];
                if (prefix < 0)
                {
                    prefix = b;
                    continue;
                }
                int key = (prefix << 8) | b;
                if (dict.TryGetValue(key, out int code))
                {
                    prefix = code;
                    continue;
                }
                WriteBits(prefix);
                if (nextFree < MaxFree)
                {
                    dict[key] = nextFree;
                    nextFree++;
                    if (nextFree > maxCode && codeWidth < 12)
                    {
                        codeWidth++;
                        maxCode = (1 << codeWidth) - 1;
                    }
                }
                else
                {
                    WriteBits(ClearCode);
                    ResetDict();
                }
                prefix = b;
            }
            if (prefix >= 0)
                WriteBits(prefix);
            WriteBits(EoiCode);

            if (bitCnt > 0)
                outList.Add((byte)(bitBuf << (8 - bitCnt)));
            outBuf.Write(outList.ToArray(), 0, outList.Count);
            return outBuf.ToArray();
        }

        private static ushort[] BuildGeoKeys(bool isGeographic, int wkid)
        {
            // GeoKeyDirectory: 头部 4 个 SHORT + 每条 Key 4 个 SHORT
            int numKeys = isGeographic ? 3 : 3;
            var keys = new ushort[4 + numKeys * 4];
            keys[0] = 1; // KeyDirectoryVersion
            keys[1] = 1; // KeyRevision
            keys[2] = 0; // MinorRevision
            keys[3] = (ushort)numKeys;

            if (isGeographic)
            {
                keys[4] = 1024; keys[5] = 0; keys[6] = 1; keys[7] = 2; // GTModelTypeGeoKey = ModelTypeGeographic
                keys[8] = 1025; keys[9] = 0; keys[10] = 1; keys[11] = 1; // GTRasterTypeGeoKey = PixelIsArea
                keys[12] = 2048; keys[13] = 0; keys[14] = 1; keys[15] = (ushort)wkid; // GeographicTypeGeoKey
            }
            else
            {
                keys[4] = 1024; keys[5] = 0; keys[6] = 1; keys[7] = 1; // GTModelTypeGeoKey = ModelTypeProjected
                keys[8] = 1025; keys[9] = 0; keys[10] = 1; keys[11] = 1; // GTRasterTypeGeoKey = PixelIsArea
                keys[12] = 3072; keys[13] = 0; keys[14] = 1; keys[15] = (ushort)wkid; // ProjectedCSTypeGeoKey
            }
            return keys;
        }

        private const ushort TypeShort = 3;
        private const ushort TypeLong = 4;
        private const ushort TypeRational = 5;
        private const ushort TypeDouble = 12;

        private static byte[] U16(ushort v) => new[] { (byte)(v & 0xFF), (byte)(v >> 8) };
        private static byte[] U32(uint v) => new[]
        {
            (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 24) & 0xFF)
        };
        private static byte[] D64(double v) => BitConverter.GetBytes(v);
        private static byte[] Rational(uint num, uint den)
        {
            var b = new byte[8];
            Array.Copy(U32(num), 0, b, 0, 4);
            Array.Copy(U32(den), 0, b, 4, 4);
            return b;
        }

        private static byte[] PayloadData(IfdEntry e, int width, int height, byte[] lzw,
            GeoreferenceHelper.AffineParams a, double cornerX, double cornerY, bool axisAligned, ushort[] geokeys)
        {
            if (e.InlineValue != null) return null;
            switch (e.Tag)
            {
                case 258: return new byte[] { 8, 0, 8, 0, 8, 0 };
                case 282: return Rational(300, 1);
                case 283: return Rational(300, 1);
                case 33550: // ModelPixelScale: A, -E, 0
                    {
                        var b = new byte[24];
                        Array.Copy(D64(a.A), 0, b, 0, 8);
                        Array.Copy(D64(-a.E), 0, b, 8, 8);
                        Array.Copy(D64(0.0), 0, b, 16, 8);
                        return b;
                    }
                case 33922: // ModelTiepoint: 0,0,0, cornerX,cornerY,0
                    {
                        var b = new byte[48];
                        Array.Copy(D64(0.0), 0, b, 0, 8);
                        Array.Copy(D64(0.0), 0, b, 8, 8);
                        Array.Copy(D64(0.0), 0, b, 16, 8);
                        Array.Copy(D64(cornerX), 0, b, 24, 8);
                        Array.Copy(D64(cornerY), 0, b, 32, 8);
                        Array.Copy(D64(0.0), 0, b, 40, 8);
                        return b;
                    }
                case 34264: // ModelTransformation 4x4
                    {
                        double[] m =
                        {
                            a.A, a.B, 0.0, cornerX,
                            a.D, a.E, 0.0, cornerY,
                            0.0, 0.0, 1.0, 0.0,
                            0.0, 0.0, 0.0, 1.0
                        };
                        var b = new byte[128];
                        for (int i = 0; i < 16; i++)
                            Array.Copy(D64(m[i]), 0, b, i * 8, 8);
                        return b;
                    }
                case 34735:
                    {
                        var b = new byte[geokeys.Length * 2];
                        for (int i = 0; i < geokeys.Length; i++)
                            Array.Copy(U16(geokeys[i]), 0, b, i * 2, 2);
                        return b;
                    }
                default:
                    return null;
            }
        }

        private class IfdEntry
        {
            public ushort Tag;
            public ushort Type;
            public uint Count;
            public byte[] InlineValue;
            public uint DataOffset;

            public IfdEntry(ushort tag, ushort type, uint count, byte[] inlineValue)
            {
                Tag = tag;
                Type = type;
                Count = count;
                InlineValue = inlineValue;
            }
        }
    }
}
