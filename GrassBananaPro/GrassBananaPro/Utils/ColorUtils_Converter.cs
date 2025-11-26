using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors;

namespace Glab.Utilities
{
    public static partial class ColorUtils
    {
        // method to convert from system drawing color to sixlabors color
        public static SixLabors.ImageSharp.Color ConvertColorSDtoIS(System.Drawing.Color color, bool removeAlpha = false)
        {
            if (removeAlpha)
            {
                return SixLabors.ImageSharp.Color.FromRgb(color.R, color.G, color.B);
            }
            return SixLabors.ImageSharp.Color.FromRgba(color.R, color.G, color.B, color.A);
        }

        public static System.Drawing.Color ConvertColorIStoSD(SixLabors.ImageSharp.Color color, bool removeAlpha = false)
        {
            var rgba = color.ToPixel<SixLabors.ImageSharp.PixelFormats.Rgba32>();
            if (removeAlpha)
            {
                return System.Drawing.Color.FromArgb(rgba.R, rgba.G, rgba.B);
            }
            return System.Drawing.Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
        }

        // method to convert from system drawing color to windows media color
        public static System.Windows.Media.Color ConvertColorSDtoSW(System.Drawing.Color color, bool removeAlpha = false)
        {
            if (removeAlpha)
            {
                return System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);
            }
            return System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        // method to convert from windows media color to system drawing color
        public static System.Drawing.Color ConvertColorSWtoSD(System.Windows.Media.Color color, bool removeAlpha = false)
        {
            if (removeAlpha)
            {
                return System.Drawing.Color.FromArgb(color.R, color.G, color.B);
            }
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public static string FormatColor(System.Drawing.Color color)
        {
            return $"{color.R}, {color.G}, {color.B} ({color.A})";
        }
        // method to convert from sixlabors color to system drawing color
        
    }
}
