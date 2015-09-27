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
            public uint Offset;
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

        static void Main(string[] args)
        {
            List<BitmapInfo> imageInfo = new List<BitmapInfo>();
            const string enumPrefix = "PREZR_IMAGE_INDEX_";
            const int version = 1;
            const int headerSize = 12;
            uint blobSize = 0;

            var files = Directory.EnumerateFiles(".", "*.png");
            foreach (var filename in files)
            {
                uint bytesWritten = sizeof(UInt32);

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

                    Console.WriteLine($"{imageName} has {colorHistogram.Count} colors.");

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
                    bi.Offset = bytesWritten;
                    imageInfo.Add(bi);

                    int pixelDataSize = pixels.Length;
                    int paletteSize = (palette != null) ? palette.Length : 0;
                    bytesWritten += (uint)(headerSize + pixelDataSize + paletteSize);
                }

                blobSize = bytesWritten;
            }

            UInt32 timeStamp = (UInt32)(DateTime.Now.ToFileTimeUtc() & 0xFFFFFFFF);

            using (BinaryWriter w = new BinaryWriter(new FileStream("prezr.blob", FileMode.Create, FileAccess.Write, FileShare.Write)))
            {
                w.Write(timeStamp);

                foreach (BitmapInfo bi in imageInfo)
                {
                    ushort rowByteStride = (ushort)bi.Width;
                    ushort versionAndFormat = (ushort)((version << 12) | ((int)bi.Format << 1));
                    ushort x = 0, y = 0;
                    ushort width = (ushort)bi.Width;
                    ushort height = (ushort)bi.Height;

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

            using (StreamWriter header = new StreamWriter("prezr.c"))
            {
                header.WriteLine("#include <pebble.h>");
                header.WriteLine("#include \"prezr.h\"");
                header.WriteLine();

                header.WriteLine($"static const size_t prezr_resource_offsets[{enumPrefix}COUNT] = {{");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {img.Offset}, // {img.Handle}");
                }
                header.WriteLine("};");
                header.WriteLine();

                header.WriteLine($"static prezr_bitmap_t prezr_embedded_bitmaps[{enumPrefix}COUNT] = {{");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {{ {img.Width}, {img.Height}, NULL }}, // {img.Handle}");
                }
                header.WriteLine("};");
                header.WriteLine();

                header.WriteLine($"const size_t prezr_resource_id = {timeStamp}U;");
                header.WriteLine($"const size_t prezr_resource_blob_size = {blobSize};");
                header.WriteLine("static uint8_t* prezr_resource_blob = NULL;");
                header.WriteLine();

                header.WriteLine("int prezr_init(uint32_t rid) {");
                header.WriteLine("  ResHandle h = resource_get_handle(rid);");
                header.WriteLine("  prezr_resource_blob = (uint8_t*) malloc(prezr_resource_blob_size);");
                header.WriteLine("  if (prezr_resource_blob == NULL) {");
                header.WriteLine("    APP_LOG(APP_LOG_LEVEL_DEBUG, \"[PREZR] OOM while trying to allocate %u bytes (%u available)\", prezr_resource_blob_size, heap_bytes_free());");
                header.WriteLine("    return PREZR_OUT_OF_MEMORY;");
                header.WriteLine("  }");
                header.WriteLine("  if (resource_load(h, prezr_resource_blob, prezr_resource_blob_size) != prezr_resource_blob_size) {");
                header.WriteLine("    APP_LOG(APP_LOG_LEVEL_DEBUG, \"[PREZR] Failed to load resource %u\", (unsigned) rid);");
                header.WriteLine("    return PREZR_RESOURCE_LOAD_FAIL;");
                header.WriteLine("  }");
                header.WriteLine("  if (*((size_t*)prezr_resource_blob) != prezr_resource_id) {");
                header.WriteLine("    APP_LOG(APP_LOG_LEVEL_DEBUG, \"[PREZR] Version fail: file %u vs expected %u\", *((size_t*)prezr_resource_blob), prezr_resource_id);");
                header.WriteLine("    return PREZR_VERSION_FAIL;");
                header.WriteLine("  }");
                header.WriteLine($"  for (size_t i = 0; i < {enumPrefix}COUNT; ++i) {{");
                header.WriteLine("    const uint8_t* data = prezr_resource_blob + prezr_resource_offsets[i];");
                header.WriteLine("    prezr_embedded_bitmaps[i].bitmap = gbitmap_create_with_data(data);");
                header.WriteLine("    if (prezr_embedded_bitmaps[i].bitmap == NULL) {");
                header.WriteLine("      APP_LOG(APP_LOG_LEVEL_DEBUG, \"[PREZR] Failed to create image %u at offset %u\", i, prezr_resource_offsets[i]);");
                header.WriteLine("      return (i+1);");
                header.WriteLine("    }");
                header.WriteLine("  }");
                header.WriteLine("  return PREZR_OK;");
                header.WriteLine("}");
                header.WriteLine();

                header.WriteLine("void prezr_destroy() {");
                header.WriteLine($"  for (size_t i = 0; i < {enumPrefix}COUNT; ++i) {{");
                header.WriteLine("    gbitmap_destroy(prezr_embedded_bitmaps[i].bitmap);");
                header.WriteLine("  }");
                header.WriteLine("  free(prezr_resource_blob);");
                header.WriteLine("}");
                header.WriteLine();
                
                header.WriteLine("const prezr_bitmap_t* prezr_get(prezr_image_index_t imageIndex) {");
                header.WriteLine("  return &prezr_embedded_bitmaps[imageIndex];");
                header.WriteLine("}");
                header.WriteLine();
            }

            using (StreamWriter header = new StreamWriter("prezr.h"))
            {
                header.WriteLine("#pragma once");
                header.WriteLine();

                header.WriteLine("typedef struct prezr_bitmap_s {");
                header.WriteLine("  uint16_t width;");
                header.WriteLine("  uint16_t height;");
                header.WriteLine("  GBitmap* bitmap;");
                header.WriteLine("} prezr_bitmap_t;");
                header.WriteLine();

                header.WriteLine("#define PREZR_OK 0");
                header.WriteLine("#define PREZR_RESOURCE_LOAD_FAIL -1");
                header.WriteLine("#define PREZR_VERSION_FAIL -2");
                header.WriteLine("#define PREZR_OUT_OF_MEMORY -3");
                header.WriteLine();

                header.WriteLine("typedef enum prezr_image_index_e {");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {enumPrefix}{img.Handle},");
                }
                header.WriteLine($"  {enumPrefix}COUNT");
                header.WriteLine("} prezr_image_index_t;");
                header.WriteLine();

                header.WriteLine("int prezr_init(uint32_t h);");
                header.WriteLine("void prezr_destroy();");
                header.WriteLine("const prezr_bitmap_t* prezr_get(prezr_image_index_t imageIndex);");
                header.WriteLine();
            }
        }
    }
}
