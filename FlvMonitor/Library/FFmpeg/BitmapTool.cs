using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlvMonitor.Library
{
    public class BitmapTool
    {
        public static void SaveToBitmap(int width, int height, IntPtr data, int stride, string op)
        {
            SKBitmap bp = new(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            bp.InstallPixels(info, data, stride);
            using (var ss = File.OpenWrite(op))
            {
                bp.Encode(ss, SKEncodedImageFormat.Png, 100);
            }
        }

        public static void CreateVoiceBitmap(ref List<short> values, int width, int height, string op)
        {
            int pixel_step = (int)ushort.MaxValue/height;
            SKBitmap bp = new(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            bp.Erase(SKColors.DarkGreen);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            List<int> ys = [];
            foreach (var v2 in values)
            {
                int y = Math.Clamp(height - (v2 + short.MaxValue) / pixel_step, 0, height-1);
                ys.Add(y);
            }
            for (var i = 0; i<ys.Count; i++)
            {
                bp.SetPixel(i, ys[i], SKColors.SpringGreen);
            }
           
            using (var ss = File.OpenWrite(op))
            {
                bp.Encode(ss, SKEncodedImageFormat.Png, 100);
            }
        }
    }
}
