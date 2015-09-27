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

        static void Main(string[] args)
        {
            using (StreamWriter header = new StreamWriter("prezr.h"))
            {
                header.WriteLine("// typedef struct pbi_image_s {");
                header.WriteLine("//   uint16_t rowStrideBytes;");
                header.WriteLine("//   uint16_t version;");
                header.WriteLine("//   int16_t x, y, width, height;");
                header.WriteLine("//   uint8_t data[];");
                header.WriteLine("// } pbi_image_t;");
                header.WriteLine();
                
                var files = Directory.EnumerateFiles(".", "*.png");
                foreach (var filename in files)
                {
                    using (Bitmap bmp = Bitmap.FromFile(filename) as Bitmap)
                    {
                        string handle = Path.GetFileNameWithoutExtension(filename).ToUpper();
                        ushort rowByteStride = (ushort) bmp.Width;
                        int version = 1;
                        int bitmapFormat = 1; // 8 bit
                        ushort versionAndFormat = (ushort) ((version << 12) | (bitmapFormat << 1));
                        header.WriteLine($"static const uint8_t PBI_IMAGE_{handle}[] = {{");
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
            }
        }
    }
}
