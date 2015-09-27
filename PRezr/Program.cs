using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using static System.Diagnostics.Debug;

namespace PRezr
{
    class Program
    {
        static string QuantizeChannel(int c)
        {
            // 0, 85, 170, 255
            if (c < 85) return "00";
            else if (c < 170) return "01";
            else if (c < 255) return "10";
            else return "11";
        }

        static void WritePixel(StreamWriter w, Color c)
        {
            w.Write("0b");
            w.Write(QuantizeChannel(c.A));
            w.Write(QuantizeChannel(c.R));
            w.Write(QuantizeChannel(c.G));
            w.Write(QuantizeChannel(c.B));
        }

        static byte LoWord(ushort s)
        {
            return (byte) s;
        }

        static byte HiWord(ushort s)
        {
            return (byte)(s >> 8);
        }

        struct BitmapInfo
        {
            public string Handle;
            public ushort Width;
            public ushort Height;
        };

        static void Main(string[] args)
        {
            List<BitmapInfo> imageInfo = new List<BitmapInfo>();
            const string handlePrefix = "PBI_IMAGE_";
            const string enumPrefix = "PBI_IMAGE_INDEX_";

            using (StreamWriter header = new StreamWriter("prezr.c"))
            {
                header.WriteLine("#include <pebble.h>");
                header.WriteLine("#include \"prezr.h\"");
                header.WriteLine();

                var files = Directory.EnumerateFiles(".", "*.png");
                foreach (var filename in files)
                {
                    using (Bitmap bmp = Bitmap.FromFile(filename) as Bitmap)
                    {
                        string imageName = Path.GetFileNameWithoutExtension(filename).ToUpper();

                        BitmapInfo bi = new BitmapInfo();
                        bi.Handle = imageName;
                        bi.Height = (ushort)bmp.Height;
                        bi.Width = (ushort) bmp.Width;
                        imageInfo.Add(bi);

                        string handle = handlePrefix + imageName;

                        ushort rowByteStride = (ushort) bmp.Width;
                        int version = 1;
                        int bitmapFormat = 1; // 8 bit
                        ushort versionAndFormat = (ushort) ((version << 12) | (bitmapFormat << 1));
                        header.WriteLine($"static const uint8_t {handle}[] = {{");
                        header.WriteLine($"  0x{LoWord(rowByteStride):X2}, 0x{HiWord(rowByteStride):X2}, // rowStrideBytes = {rowByteStride}");
                        header.WriteLine($"  0x{LoWord(versionAndFormat):X2}, 0x{HiWord(versionAndFormat):X2}, // version = {version}, bitmap format = {bitmapFormat}");
                        header.WriteLine($"  0x00, 0x00, // x");
                        header.WriteLine($"  0x00, 0x00, // y");
                        header.WriteLine($"  0x{LoWord((ushort)bmp.Width):X2}, 0x{HiWord((ushort)bmp.Width):X2}, // width = {bmp.Width}");
                        header.WriteLine($"  0x{LoWord((ushort)bmp.Height):X2}, 0x{HiWord((ushort)bmp.Height):X2}, // height = {bmp.Height}");
                        header.WriteLine($"  // data = {{");
                        
                        for (var y = 0; y < bmp.Height; ++y)
                        {
                            header.Write("    ");
                            for (var x = 0; x < bmp.Width; ++x)
                            {
                                WritePixel(header, bmp.GetPixel(x, y));
                                header.Write(", ");
                            }
                            header.WriteLine();
                        }

                        header.WriteLine("  //}");
                        header.WriteLine("};");
                        header.WriteLine();
                    }
                }

                header.WriteLine($"static GBitmap* prezr_embedded_bitmaps[{enumPrefix}COUNT] =");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {{ {img.Width}, {img.Height}, NULL }}, // {img.Handle}");
                }
                header.WriteLine("};");
                header.WriteLine();

                header.WriteLine("void prezr_init() {");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  prezr_embedded_bitmaps[{enumPrefix}{img.Handle}].bitmap = gbitmap_create_with_data({handlePrefix}{img.Handle});");
                }
                header.WriteLine("}");
                header.WriteLine();

                header.WriteLine("void prezr_destroy() {");
                header.WriteLine($"  size_t imgCount = {enumPrefix}COUNT;");
                header.WriteLine("  for (int i = 0; i < imgCount; ++i) {");
                header.WriteLine("    gbitmap_destroy(prezr_embedded_bitmaps[i].bitmap);");
                header.WriteLine("  }");
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

                header.WriteLine("typedef enum prezr_image_index_e {");
                foreach (var img in imageInfo)
                {
                    header.WriteLine($"  {enumPrefix}{img.Handle},");
                }
                header.WriteLine($"  {enumPrefix}COUNT");
                header.WriteLine("} prezr_image_index_t;");
                header.WriteLine();

                header.WriteLine("void prezr_init();");
                header.WriteLine("void prezr_destroy();");
                header.WriteLine("const prezr_bitmap_t* prezr_get(prezr_image_index_t imageIndex);");
                header.WriteLine();
            }
        }
    }
}
