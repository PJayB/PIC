using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace PIC
{
    class Program
    {
        class ImageData : IDisposable
        {
            private Bitmap _bitmap;
            private BitmapData _data;
            private ImageLockMode _mode;

            public ImageData(Bitmap bmp, ImageLockMode mode)
            {
                _bitmap = bmp;
                _mode = mode;
                _data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), mode, bmp.PixelFormat);
            }

            public void Dispose()
            {
                _bitmap.UnlockBits(_data);
            }

            private Bitmap Bitmap { get { return _bitmap; } }
            public int Width { get { return _data.Width; } }
            public int Height { get { return _data.Height; } }

            public Color GetPixel(int x, int y)
            {
                if (_mode == ImageLockMode.WriteOnly)
                    throw new Exception("Can't read from this image. It is writeonly.");

                unsafe
                {
                    int columnOffset = x * 4;
                    byte* row = (byte*)_data.Scan0 + (y * _data.Stride);
                    byte B = row[columnOffset];
                    byte G = row[columnOffset + 1];
                    byte R = row[columnOffset + 2];
                    byte alpha = row[columnOffset + 3];

                    return Color.FromArgb(alpha, R, G, B);
                }
            }

            public void SetPixel(int x, int y, Color c)
            {
                if (_mode == ImageLockMode.ReadOnly)
                    throw new Exception("Can't write to this image. It is readonly.");

                unsafe
                {
                    int columnOffset = x * 4;
                    byte* row = (byte*)_data.Scan0 + (y * _data.Stride);
                    row[columnOffset] = c.B;
                    row[columnOffset + 1] = c.G;
                    row[columnOffset + 2] = c.R;
                    row[columnOffset + 3] = c.A;
                }
            }
        }

        class DitherPattern
        {
            public readonly int Divisor;
            public readonly int[][] Kernel;
            public readonly int KernelMinX;
            public readonly int KernelMaxX;
            public readonly int KernelHeight;

            public DitherPattern(int divisor, int[][] kernel)
            {
                Divisor = divisor;
                Kernel = kernel;

                int kernelWidth = 0;

                foreach (int[] row in Kernel)
                {
                    if (kernelWidth == 0)
                        kernelWidth = 1 + 2 * row.Length;
                    else if (row.Length != kernelWidth)
                        throw new Exception("Diffusion Kernel cannot be variable width");
                }

                KernelMinX = -kernel[0].Length;
                KernelMaxX = kernel[0].Length;
                KernelHeight = kernel.Length;
            }
        };

        static readonly DitherPattern FloydSteinbergDither = new DitherPattern(
            16,
            new int[][]
            {
                new int[] {       7 },
                new int[] { 3, 5, 1 }
            }
        );

        static readonly DitherPattern JarvisJudiceNinkeDither = new DitherPattern(
            48,
            new int[][]
            {
                new int[] {          7, 5 },
                new int[] { 3, 5, 7, 5, 3 },
                new int[] { 1, 3, 5, 3, 1 }
            }
        );

        static readonly DitherPattern AtkinsonDither = new DitherPattern(
            8,
            new int[][]
            {
                new int[] {          1, 1 },
                new int[] { 0, 1, 1, 1, 0 },
                new int[] { 0, 0, 1, 0, 0 }
            }
        );

        static readonly DitherPattern TwoRowSierraDither = new DitherPattern(
            32,
            new int[][]
            {
                new int[] {          5, 3 },
                new int[] { 2, 4, 5, 4, 2 },
                new int[] { 0, 2, 3, 2, 0 }
            }
        );

        static readonly DitherPattern SierraDither = new DitherPattern(
            16,
            new int[][]
            {
                new int[] {          4, 3 },
                new int[] { 1, 2, 3, 2, 1 }
            }
        );

        static readonly DitherPattern SierraLiteDither = new DitherPattern(
            16,
            new int[][]
            {
                new int[] {       2 },
                new int[] { 1, 1, 0 }
            }
        );

        struct SignedColor
        {
            public int A;
            public int R;
            public int G;
            public int B;

            public SignedColor(int a)
            {
                A = a;
                R = a;
                G = a;
                B = a;
            }

            public SignedColor(int a, int r, int g, int b)
            {
                A = a;
                R = r;
                G = g;
                B = b;
            }

            public void Add(SignedColor c)
            {
                A += c.A;
                R += c.R;
                G += c.G;
                B += c.B;
            }
        }

        class Ditherer
        {
            int _width;
            int _height;
            SignedColor[] _errorMatrix;

            public Ditherer(int width, int height)
            {
                _width = width;
                _height = height;
                _errorMatrix = new SignedColor[width * height];
            }

            private SignedColor GetError(int x, int y)
            {
                if (x >= 0 && x < _width && y >= 0 && y < _height)
                    return _errorMatrix[y * _width + x];
                else
                    return new SignedColor(0);
            }

            private void SetError(int x, int y, SignedColor c)
            {
                if (x >= 0 && x < _width && y >= 0 && y < _height)
                    _errorMatrix[y * _width + x] = c;
            }

            private void AddError(int x, int y, SignedColor c)
            {
                if (x >= 0 && x < _width && y >= 0 && y < _height)
                    _errorMatrix[y * _width + x].Add(c);
            }

            private SignedColor Diff(Color a, Color b)
            {
                return new SignedColor(
                    a.A - b.A,
                    a.R - b.R,
                    a.G - b.G,
                    a.B - b.B);
            }

            static int QuantizeChannel(int c)
            {
                // 0, 85, 170, 255
                if (c < 42) return 0;
                else if (c < 128) return 85;
                else if (c < 213) return 170;
                else return 255;
            }

            static Color QuantizeColor(Color c, SignedColor e)
            {
                int r = QuantizeChannel(c.R + e.R);
                int g = QuantizeChannel(c.G + e.G);
                int b = QuantizeChannel(c.B + e.B);
                int a = QuantizeChannel(c.A + e.A);
                return Color.FromArgb(a, r, g, b);
            }

            static SignedColor CalculateError(SignedColor delta, int x, int y, DitherPattern pattern)
            {
                if (y == 0) { x--; }
                else { x -= pattern.KernelMinX; }

                int mul = pattern.Kernel[y][x];
                
                if (mul == 0)
                    return new SignedColor(0);
                else
                {
                    int div = pattern.Divisor;
                    SignedColor c = new SignedColor();
                    c.R = (delta.R * mul) / div;
                    c.G = (delta.G * mul) / div;
                    c.B = (delta.B * mul) / div;
                    c.A = (delta.A * mul) / div;
                    return c;
                }
            }

            public Color Dither(int x, int y, Color c, DitherPattern pattern)
            {
                // Get the error at this pixel
                SignedColor e = GetError(x, y);

                // Get the quantized color
                Color q = QuantizeColor(c, e);

                // Calculate the difference
                SignedColor delta = Diff(c, q);

                // First row handled differently
                for (int i = 1; i <= pattern.KernelMaxX; ++i)
                {
                    AddError(x + i, y, CalculateError(delta, i, 0, pattern));
                }

                // Process subsequent rows
                for (int j = 1; j < pattern.KernelHeight; ++j)
                {
                    for (int i = pattern.KernelMinX; i <= pattern.KernelMaxX; ++i)
                    {
                        AddError(x + i, y + j, CalculateError(delta, i, j, pattern));
                    }
                }

                return q;
            }
        }

        static void DoOneImage(string filename, Color transparentColorKey)
        {
            string previewFile = Path.Combine(Path.GetDirectoryName(filename), "preview", Path.GetFileNameWithoutExtension(filename) + "_preview.png");

            HashSet<Color> colorHistagram = new HashSet<Color>();

            Bitmap bmp = Bitmap.FromFile(filename) as Bitmap;
            Bitmap previewBmp = Bitmap.FromFile(filename) as Bitmap;
            Ditherer ditherer = new Ditherer(bmp.Width, bmp.Height);

            using (ImageData srcData = new ImageData(bmp, ImageLockMode.ReadOnly))
            {
                using (ImageData previewData = new ImageData(previewBmp, ImageLockMode.WriteOnly))
                {
                    for (int x = 0; x < srcData.Width; x++)
                    {
                        for (int y = 0; y < srcData.Height; y++)
                        {
                            Color c = srcData.GetPixel(x, y);

                            if (!colorHistagram.Contains(c))
                                colorHistagram.Add(c);

                            if (c == transparentColorKey || c.A == 0)
                            {
                                previewData.SetPixel(x, y, Color.FromArgb(0));
                            }
                            else
                            {
                                previewData.SetPixel(x, y, ditherer.Dither(x, y, c, FloydSteinbergDither));
                            }
                        }
                    }
                }
            }
            
            previewBmp.Save(previewFile);

            // Statistics
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(filename)} contains {colorHistagram.Count} unique colors.");
        }
        
        static void Main(string[] args)
        {            
            Directory.CreateDirectory("preview");

            Color transparentColorKey = Color.FromArgb(255, 0, 255, 255);

            // Enumerate the current directory
            if (args.Length == 0)
            {
                var files = Directory.EnumerateFiles(".", "*.png");
                foreach (var file in files)
                {
                    DoOneImage(file, transparentColorKey);
                }
            }
            else
            {
                DoOneImage(args[0], transparentColorKey);
            }
        }
    }
}
