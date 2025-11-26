using Glab.Utilities;
using Rhino.Geometry;
using IS = SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SD = System.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp;

namespace Glab.C_Documentation
{
    public class GImage
    {
        // Properties
        public IS.Rectangle ISRectangle { get; set; }
        public Rectangle3d RhinoRectangle { get; set; }
        public IS.Image<Rgba32> Image { get; set; }
        public SD.Color Color { get; set; }

        // Constructor
        public GImage(IS.Rectangle rectangle, SD.Color color = default)
        {
            ISRectangle = rectangle;
            Color = color == default ? SD.Color.White : color;
            CreateInitialImage();
        }

        // Constructor overload that takes a Rhino.Geometry.Rectangle3d
        public GImage(Rectangle3d rectangle, SD.Color color = default)
        {
            RhinoRectangle = rectangle;

            // Convert Rhino Rectangle3d to SixLabors.ImageSharp.Rectangle using the new converter
            ISRectangle = ImageUtils.RhinoRect3dToISRect(rectangle);

            Color = color == default ? SD.Color.White : color;
            CreateInitialImage();
        }

        // Method
        public void CreateInitialImage()
        {
            // Get width and height from rectangle
            int width = (int)ISRectangle.Width;
            int height = (int)ISRectangle.Height;

            // Create a new image with dimensions from rectangle
            Image = new Image<Rgba32>(width, height);

            // If a background color was specified, fill the image with it
            if (Color != default)
            {
                // Convert SD.Color to SixLabors.ImageSharp.Color using the utility method
                var pixelColor = ColorUtils.ConvertColorSDtoIS(Color);

                // Fill the entire image with the background color
                Image.Mutate(x => x.BackgroundColor(pixelColor));
            }
        }
    }
}
