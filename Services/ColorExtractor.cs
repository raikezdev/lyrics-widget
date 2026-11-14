using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LyricsWidget.Services
{
    public static class ColorExtractor
    {
        public static (Color Primary, Color Accent) ExtractVibrantColors(byte[]? artBytes)
        {
            if (artBytes == null || artBytes.Length == 0)
            {
                return (Color.FromRgb(0xFD, 0xBA, 0x25), Color.FromRgb(0xF5, 0x9E, 0x0B));
            }

            try
            {
                using var ms = new MemoryStream(artBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.DecodePixelWidth = 32;
                bmp.DecodePixelHeight = 32;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                var wb = new WriteableBitmap(bmp);
                int width = wb.PixelWidth;
                int height = wb.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                wb.CopyPixels(pixels, stride, 0);

                double bestScore = -1;
                Color bestColor = Color.FromRgb(0xFD, 0xBA, 0x25);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    ColorToHsv(r, g, b, out _, out double s, out double v);

                    if (v < 0.25 || v > 0.95 || s < 0.35) continue;

                    double score = s * 0.7 + v * 0.3;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestColor = Color.FromRgb(r, g, b);
                    }
                }

                if (bestScore > 0)
                {
                    Color accent = Color.FromRgb(
                        (byte)Math.Clamp(bestColor.R + 30, 0, 255),
                        (byte)Math.Clamp(bestColor.G + 25, 0, 255),
                        (byte)Math.Clamp(bestColor.B + 15, 0, 255)
                    );
                    return (bestColor, accent);
                }
            }
            catch { }

            return (Color.FromRgb(0xFD, 0xBA, 0x25), Color.FromRgb(0xF5, 0x9E, 0x0B));
        }

        private static void ColorToHsv(byte r, byte g, byte b, out double hue, out double saturation, out double value)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;

            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));

            value = max;
            double delta = max - min;
            saturation = max == 0 ? 0 : delta / max;

            if (delta == 0)
            {
                hue = 0;
            }
            else
            {
                if (rd == max)
                    hue = (gd - bd) / delta + (gd < bd ? 6 : 0);
                else if (gd == max)
                    hue = (bd - rd) / delta + 2;
                else
                    hue = (rd - gd) / delta + 4;

                hue *= 60;
            }
        }
    }
}
