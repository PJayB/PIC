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
            Bit1Palettized = 2,
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

        static void Compress1BitPalette(byte[] srcData, int srcRowStride, out byte[] dstData, out byte[] palette)
        {
            Dictionary<byte, int> colorMap = new Dictionary<byte, int>();
            for (int i = 0; i < srcData.Length; ++i)
            {
                if (!colorMap.ContainsKey(srcData[i]))
                    colorMap.Add(srcData[i], colorMap.Count);
            }

            int numRows = srcData.Length / srcRowStride;
            Debug.Assert(numRows * srcRowStride == srcData.Length);
            Debug.Assert(colorMap.Count <= 2);

            int dstRowStride = (srcRowStride + 7) / 8;

            dstData = new byte[dstRowStride * numRows];
            for (int j = 0, srcRowOffset = 0, dstRowOffset = 0;
                j < numRows;
                ++j, srcRowOffset += srcRowStride, dstRowOffset += dstRowStride)
            {
                for (int i = 0; i < srcRowStride; i += 8)
                {
                    int b = 7;
                    byte v = 0;
                    for (int k = 0; k < 8 && i + k < srcRowStride; ++k, --b)
                    {
                        int index = colorMap[srcData[srcRowOffset + i + k]];
                        v |= (byte)((index & 1) << b);
                    }
                    dstData[dstRowOffset + i / 8] = v;
                }
            }


            palette = new byte[colorMap.Count];
            foreach (var kvp in colorMap)
            {
                palette[kvp.Value] = kvp.Key;
            }
        }


        static void Compress2BitPalette(byte[] srcData, int srcRowStride, out byte[] dstData, out byte[] palette)
        {
            Dictionary<byte, int> colorMap = new Dictionary<byte, int>();
            for (int i = 0; i < srcData.Length; ++i)
            {
                if (!colorMap.ContainsKey(srcData[i]))
                    colorMap.Add(srcData[i], colorMap.Count);
            }

            int numRows = srcData.Length / srcRowStride;
            Debug.Assert(numRows * srcRowStride == srcData.Length);
            Debug.Assert(colorMap.Count <= 4);

            int dstRowStride = (srcRowStride + 3) / 4;

            dstData = new byte[dstRowStride * numRows];
            for (int j = 0, srcRowOffset = 0, dstRowOffset = 0;
                j < numRows;
                ++j, srcRowOffset += srcRowStride, dstRowOffset += dstRowStride)
            {
                for (int i = 0; i < srcRowStride; i += 4)
                {
                    int index = colorMap[srcData[srcRowOffset + i]];
                    byte v = (byte)(index << 6);

                    if (i + 1 < srcRowStride)
                    {
                        index = colorMap[srcData[srcRowOffset + i + 1]];
                        v |= (byte)(index << 4);
                    }

                    if (i + 2 < srcRowStride)
                    {
                        index = colorMap[srcData[srcRowOffset + i + 2]];
                        v |= (byte)(index << 2);
                    }

                    if (i + 3 < srcRowStride)
                    {
                        index = colorMap[srcData[srcRowOffset + i + 3]];
                        v |= (byte)(index);
                    }

                    dstData[dstRowOffset + i / 4] = v;
                }
            }


            palette = new byte[colorMap.Count];
            foreach (var kvp in colorMap)
            {
                palette[kvp.Value] = kvp.Key;
            }
        }

        static void Compress4BitPalette(byte[] srcData, int srcRowStride, out byte[] dstData, out byte[] palette)
        {
            Dictionary<byte, int> colorMap = new Dictionary<byte, int>();
            for (int i = 0; i < srcData.Length; ++i)
            {
                if (!colorMap.ContainsKey(srcData[i]))
                    colorMap.Add(srcData[i], colorMap.Count);
            }

            int numRows = srcData.Length / srcRowStride;
            Debug.Assert(numRows * srcRowStride == srcData.Length);
            Debug.Assert(colorMap.Count <= 16);

            int dstRowStride = (srcRowStride + 1) / 2;
            
            dstData = new byte[dstRowStride * numRows];
            for (int j = 0, srcRowOffset = 0, dstRowOffset = 0;
                j < numRows;
                ++j, srcRowOffset += srcRowStride, dstRowOffset += dstRowStride)
            {
                for (int i = 0; i < srcRowStride; i += 2)
                {
                    int index = colorMap[srcData[srcRowOffset + i]];
                    byte v = (byte)(index << 4);
                    if (i + 1 < srcRowStride)
                    {
                        index = colorMap[srcData[srcRowOffset + i + 1]];
                        v |= (byte)(index);
                    }

                    dstData[dstRowOffset + i / 2] = v;
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

                    if (colorHistogram.Count <= 2)
                    {
                        // Encode in 4 bits
                        Compress1BitPalette(pixels, bmp.Width, out pixels, out palette);

                        bi.Palette = palette;
                        bi.Pixels = pixels;
                        bi.Format = PblPixelFormat.Bit1Palettized;
                    }
                    else if (colorHistogram.Count <= 4)
                    {
                        // Encode in 4 bits
                        Compress2BitPalette(pixels, bmp.Width, out pixels, out palette);

                        bi.Palette = palette;
                        bi.Pixels = pixels;
                        bi.Format = PblPixelFormat.Bit2Palettized;
                    }
                    else if (colorHistogram.Count <= 16)
                    {
                        // Encode in 4 bits
                        Compress4BitPalette(pixels, bmp.Width, out pixels, out palette);

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
                    ushort rowByteStride;
                    switch (bi.Format)
                    {
                        case PblPixelFormat.Bit4Palettized:
                            rowByteStride = (ushort)((bi.Width + 1) / 2);
                            break;
                        case PblPixelFormat.Bit2Palettized:
                            rowByteStride = (ushort)((bi.Width + 3) / 4);
                            break;
                        default:
                            rowByteStride = (ushort)bi.Width;
                            break;
                    }

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

            using (StreamWriter header = new StreamWriter("prezr.packages.h", true))
            {
                header.WriteLine($"// ------------------------- {packageName} -------------------------");
                header.WriteLine($"#define {enumPrefix}CHECKSUM 0x{timeStamp:X}");
                header.WriteLine();

                header.WriteLine($"typedef enum prezr_pack_{packageName}_e {{");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {enumPrefix}{img.Handle}, // {img.Width}x{img.Height} {img.Format}");
                }
                header.WriteLine($"  {enumPrefix}COUNT");
                header.WriteLine($"}} prezr_pack_{packageName}_t;");
                header.WriteLine();

                string resourceID = $"RESOURCE_ID_{enumPrefix}PACK";
                header.WriteLine($"#if defined(PREZR_IMPORT) || defined(PREZR_IMPORT_{packageName.ToUpper()}_PACK)");
                header.WriteLine($"prezr_pack_t prezr_{packageName} = {{ NULL, 0, NULL }};");
                header.WriteLine($"void prezr_load_{packageName}() {{");
                header.WriteLine($"  int r = prezr_init(&prezr_{packageName}, {resourceID});");
                header.WriteLine($"  if (r != PREZR_OK)");
                header.WriteLine($"    APP_LOG(APP_LOG_LEVEL_ERROR, \"PRezr package '{packageName}' failed with code %d\", r);");
                header.WriteLine($"  if (prezr_{packageName}.numResources != {enumPrefix}COUNT)");
                header.WriteLine($"    APP_LOG(APP_LOG_LEVEL_ERROR, \"PRezr package '{packageName}' resource count mismatch\");");
                header.WriteLine("}");
                header.WriteLine($"void prezr_unload_{packageName}() {{");
                header.WriteLine($"  prezr_destroy(&prezr_{packageName});");
                header.WriteLine("}");
                header.WriteLine("#else");
                header.WriteLine($"extern prezr_pack_t prezr_{packageName};");
                header.WriteLine($"extern void prezr_load_{packageName}();");
                header.WriteLine($"extern void prezr_unload_{packageName}();");
                header.WriteLine("#endif // PREZR_IMPORT");
                header.WriteLine();
            }
        }

        static void Main(string[] args)
        {
            using (StreamWriter header = new StreamWriter("prezr.packages.h"))
            {
                header.WriteLine("#pragma once");
                header.WriteLine();
            }

            var subdirs = Directory.EnumerateDirectories(".");
            foreach (var dir in subdirs)
            {
                DoDirectory(dir);
            }
        }
    }
}
