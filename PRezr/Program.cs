using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using static System.Diagnostics.Debug;

namespace PRezr
{
    class Program
    {
        static int QuantizeChannel(int c)
        {
            // 0, 85, 170, 255
            if (c < 85) return 0;
            else if (c < 170) return 1;
            else if (c < 255) return 2;
            else return 3;
        }

        static byte PackPixel(Color c)
        {
            return (byte)( (QuantizeChannel(c.A) << 6) |
                           (QuantizeChannel(c.R) << 4) |
                           (QuantizeChannel(c.G) << 2) |
                           (QuantizeChannel(c.B)));
        }

        enum PblPixelFormat
        {
            Bit8 = 1,
            Bit2Palettized = 3,
            Bit4Palettized = 4
        }

        class BitmapInfo
        {
            public string Handle;
            public ushort Width;
            public ushort Height;
            public byte[] Pixels;
            public byte[] Palette;
            public PblPixelFormat Format;
        };

        static void Compress2BitPalette(byte[] srcData, out byte[] dstData, out byte[] palette)
        {
            Dictionary<byte, int> colorMap = new Dictionary<byte, int>();
            for (int i = 0; i < srcData.Length; ++i)
            {
                if (!colorMap.ContainsKey(srcData[i]))
                    colorMap.Add(srcData[i], colorMap.Count);
            }

            Debug.Assert(colorMap.Count <= 4);

            dstData = new byte[(srcData.Length + 3) / 4];
            for (int i = 0; i < dstData.Length; i += 4)
            {
                int index = colorMap[srcData[i]];
                byte v = (byte)(index << 6);

                if (i+1 < srcData.Length)
                {
                    index = colorMap[srcData[i + 1]];
                    v |= (byte)(index << 4);
                }

                if (i + 2 < srcData.Length)
                {
                    index = colorMap[srcData[i + 2]];
                    v |= (byte)(index << 2);
                }

                if (i + 3 < srcData.Length)
                {
                    index = colorMap[srcData[i + 3]];
                    v |= (byte)(index);
                }
            }

            palette = new byte[colorMap.Count];
            foreach (var kvp in colorMap)
            {
                palette[kvp.Value] = kvp.Key;
            }
        }

        static void Compress4BitPalette(byte[] srcData, out byte[] dstData, out byte[] palette)
        {
            Dictionary<byte, int> colorMap = new Dictionary<byte, int>();
            for (int i = 0; i < srcData.Length; ++i)
            {
                if (!colorMap.ContainsKey(srcData[i]))
                    colorMap.Add(srcData[i], colorMap.Count);
            }

            Debug.Assert(colorMap.Count <= 16);

            dstData = new byte[(srcData.Length + 1) / 2];
            for (int i = 0; i < dstData.Length; i += 2)
            {
                int index = colorMap[srcData[i]];
                byte v = (byte)(index << 4);
                if (i+1 < srcData.Length)
                {
                    index = colorMap[srcData[i + 1]];
                    v |= (byte)(index);
                }
            }

            palette = new byte[colorMap.Count];
            foreach (var kvp in colorMap)
            {
                palette[kvp.Value] = kvp.Key;
            }
        }

        static void DoDirectory(string path)
        {
            string packageName = Path.GetFileNameWithoutExtension(path).ToLower();
            List<BitmapInfo> imageInfo = new List<BitmapInfo>();
            string enumPrefix = $"PREZR_{packageName.ToUpper()}_";
            const int version = 1;
            const int imgHeaderSize = sizeof(UInt16) * 6; // pbi header
            const int packHeaderSize = sizeof(UInt32) * 2; // sizeof(prezr_pack_t)
            const int resHeaderSize = sizeof(UInt32) * 2; // sizeof(prezr_bitmap_t)

            Console.WriteLine($"Package '{packageName}'...");

            var files = Directory.EnumerateFiles(path, "*.png");
            foreach (var filename in files)
            {
                using (Bitmap bmp = Bitmap.FromFile(filename) as Bitmap)
                {
                    string imageName = Path.GetFileNameWithoutExtension(filename).ToUpper();

                    HashSet<byte> colorHistogram = new HashSet<byte>();

                    byte[] pixels = new byte[bmp.Width * bmp.Height];

                    for (int y = 0; y < bmp.Height; ++y)
                    {
                        for (int x = 0; x < bmp.Width; ++x)
                        {
                            byte p = PackPixel(bmp.GetPixel(x, y));
                            pixels[y * bmp.Width + x] = p;

                            if (!colorHistogram.Contains(p))
                                colorHistogram.Add(p);
                        }
                    }

                    Console.WriteLine($" .. {imageName} has {colorHistogram.Count} colors.");

                    byte[] palette = null;
                    BitmapInfo bi = new BitmapInfo();

                    if (colorHistogram.Count <= 4)
                    {
                        // Encode in 4 bits
                        Compress2BitPalette(pixels, out pixels, out palette);

                        bi.Palette = palette;
                        bi.Pixels = pixels;
                        bi.Format = PblPixelFormat.Bit2Palettized;
                    }
                    else if (colorHistogram.Count <= 16)
                    {
                        // Encode in 4 bits
                        Compress4BitPalette(pixels, out pixels, out palette);

                        bi.Palette = palette;
                        bi.Pixels = pixels;
                        bi.Format = PblPixelFormat.Bit4Palettized;
                    }
                    else
                    {
                        // Encode in 8 bits
                        bi.Pixels = pixels;
                        bi.Format = PblPixelFormat.Bit8;
                    }

                    bi.Handle = imageName;
                    bi.Height = (ushort)bmp.Height;
                    bi.Width = (ushort)bmp.Width;
                    imageInfo.Add(bi);
                }
            }

            UInt32 timeStamp = (UInt32)(DateTime.Now.ToFileTimeUtc() & 0xFFFFFFFF);

            using (BinaryWriter w = new BinaryWriter(new FileStream($"prezr.{packageName}.blob", FileMode.Create, FileAccess.Write, FileShare.Write)))
            {
                w.Write((UInt32) timeStamp); // timestamp
                w.Write((UInt32) imageInfo.Count); // numResources

                // Write headers
                long basePos = packHeaderSize + resHeaderSize * imageInfo.Count;
                foreach (BitmapInfo bi in imageInfo)
                {
                    w.Write((UInt16)bi.Width); // width
                    w.Write((UInt16)bi.Height); // height
                    w.Write((UInt32)basePos); // offset

                    long imageDataSize = bi.Pixels.Length;
                    if (bi.Palette != null)
                    {
                        imageDataSize += bi.Palette.Length;
                    }
                    basePos += imgHeaderSize + imageDataSize;
                }

#if DEBUG
                w.Flush();
#endif

                // Write the image data
                foreach (BitmapInfo bi in imageInfo)
                {
                    ushort rowByteStride = (ushort)bi.Width;
                    ushort versionAndFormat = (ushort)((version << 12) | ((int)bi.Format << 1));
                    ushort x = 0, y = 0;
                    ushort width = (ushort)bi.Width;
                    ushort height = (ushort)bi.Height;

                    // raw pbi data
                    w.Write(rowByteStride);
                    w.Write(versionAndFormat);
                    w.Write(x);
                    w.Write(y);
                    w.Write(width);
                    w.Write(height);

                    w.Write(bi.Pixels);

                    if (bi.Palette != null)
                        w.Write(bi.Palette);
                }
            }

            using (StreamWriter header = new StreamWriter($"prezr.{packageName}.h"))
            {
                header.WriteLine("#include <pebble.h>");
                header.WriteLine("#include \"prezr.h\"");
                header.WriteLine();

                header.WriteLine($"#define {enumPrefix}CHECKSUM 0x{timeStamp:X}");
                header.WriteLine();

                header.WriteLine($"enum prezr_pack_{packageName} = {{");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {enumPrefix}{img.Handle},");
                }
                header.WriteLine("};");
                header.WriteLine();
            }
        }

        static void Main(string[] args)
        {
            var subdirs = Directory.EnumerateDirectories(".");
            foreach (var dir in subdirs)
            {
                DoDirectory(dir);
            }
        }
    }
}
