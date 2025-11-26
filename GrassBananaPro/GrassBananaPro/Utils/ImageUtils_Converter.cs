using System;
using System.Collections.Generic;
using SD = System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Glab.C_Documentation;
using Grasshopper.Kernel;
using Rhino.Geometry;
using SixLabors.Fonts;
using IS = SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using SLF = SixLabors.Fonts;

namespace Glab.Utilities
{
    public partial class ImageUtils
    {
        /// <summary>
        /// Converts a list of Rhino.Geometry.Curve objects to a single SixLabors.ImageSharp.Drawing.IPath
        /// </summary>
        /// <param name="rhinoCurves">The Rhino curves to convert</param>
        /// <returns>An IPath that can be used with ImageSharp drawing operations</returns>
        public static IPath ConvertRhinoCurvesToPath(IEnumerable<Curve> rhinoCurves)
        {
            if (rhinoCurves == null || !rhinoCurves.Any())
                return null;

            // Create a new path builder
            var pathBuilder = new PathBuilder();

            foreach (var rhinoCurve in rhinoCurves)
            {
                if (rhinoCurve == null)
                    continue;

                Polyline polyline;
                // If the curve is not a polyline, convert it to one.
                if (!rhinoCurve.TryGetPolyline(out polyline))
                {
                    // ToPolyline returns a PolylineCurve, from which we get the polyline
                    var polylineCurve = rhinoCurve.ToPolyline(0.01, 1.0, 0, 0);
                    if (polylineCurve == null || !polylineCurve.TryGetPolyline(out polyline))
                    {
                        continue; // Skip if conversion fails
                    }
                }

                if (polyline != null && polyline.Count >= 2)
                {
                    // Start a new figure for the continuous polyline
                    pathBuilder.StartFigure();
                    pathBuilder.MoveTo(new PointF((float)polyline[0].X, (float)polyline[0].Y));
                    for (int i = 1; i < polyline.Count; i++)
                    {
                        pathBuilder.LineTo(new PointF((float)polyline[i].X, (float)polyline[i].Y));
                    }

                    // If the original curve was closed, close the figure to ensure a seamless path
                    if (rhinoCurve.IsClosed)
                    {
                        pathBuilder.CloseFigure();
                    }
                }
            }

            return pathBuilder.Build();
        }

        /// <summary>
        /// Converts a SD.Image to a SixLabors.ImageSharp.Image<Rgba32>
        /// </summary>
        /// <param name="drawingImage">The SD.Image to convert</param>
        /// <returns>An ImageSharp Image with alpha channel support</returns>
        public static IS.Image<Rgba32> ConvertToISImage(SD.Image drawingImage)
        {
            if (drawingImage == null)
                return null;

            using (var memoryStream = new MemoryStream())
            {
                // Save the SD.Image to a memory stream
                drawingImage.Save(memoryStream, SD.Imaging.ImageFormat.Png);
                memoryStream.Position = 0;

                // Load the memory stream into an ImageSharp Image
                return IS.Image.Load<Rgba32>(memoryStream);
            }
        }

        /// <summary>
        /// Converts a SixLabors.ImageSharp.Image<Rgba32> to a SD.Image
        /// </summary>
        /// <param name="imageSharpImage">The ImageSharp Image to convert</param>
        /// <returns>A SD.Image with alpha channel preserved</returns>
        public static SD.Image ConvertToSDImage(IS.Image<Rgba32> imageSharpImage)
        {
            if (imageSharpImage == null)
                return null;

            using (var memoryStream = new MemoryStream())
            {
                // Save the ImageSharp Image to a memory stream as PNG to preserve alpha
                imageSharpImage.Save(memoryStream, new IS.Formats.Png.PngEncoder());
                memoryStream.Position = 0;

                // Load the memory stream into a SD.Image
                return SD.Image.FromStream(memoryStream);
            }
        }

        public static IS.Rectangle RhinoRect3dToISRect(Rectangle3d rectangle)
        {
            if (!rectangle.IsValid)
            {
                throw new ArgumentException("The provided Rectangle3d is not valid.");
            }

            Point3d corner = rectangle.Corner(0);
            return new IS.Rectangle(
                (int)Math.Round(corner.X),
                (int)Math.Round(corner.Y),
                (int)Math.Round(rectangle.Width),
                (int)Math.Round(rectangle.Height)
            );
        }

        
    }
}
