using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IS = SixLabors.ImageSharp;
using SD = System.Drawing;
using Glab.Utilities;

namespace Glab.C_Documentation
{
    public class LineSetting
    {
        //porperties
        public int LineWidth { get; set; }
        public int[] LinePattern { get; set; }
        public IS.Color LineColor { get; set; } = default;
        public SD.Color LineColorSD { get; set; } = default;

        //constructor
        public LineSetting(int lineWidth, int[] linePattern)
            : this(lineWidth = 2, linePattern = null, IS.Color.Black)
        {
            // convert IS.Color to SD.Color
            LineColorSD = ColorUtils.ConvertColorIStoSD(LineColor);
        }

        public LineSetting(int lineWidth, int[] linePattern, IS.Color lineColor)
        {
            LineWidth = lineWidth;
            LinePattern = linePattern;
            LineColor = lineColor;

            // convert IS.Color to SD.Color
            LineColorSD = ColorUtils.ConvertColorIStoSD(LineColor);
        }

        public LineSetting(int lineWidth, int[] linePattern, SD.Color lineColor)
        {
            LineWidth = lineWidth;
            LinePattern = linePattern;
            LineColorSD = lineColor;

            // convert IS.Color to SD.Color
            LineColor = ColorUtils.ConvertColorSDtoIS(LineColorSD);
        }

    }
}
