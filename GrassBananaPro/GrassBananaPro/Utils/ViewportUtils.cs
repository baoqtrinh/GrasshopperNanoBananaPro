using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Drawing;
using Glab.Utilities;
using SixLabors.ImageSharp.Processing;
using Rhino.Geometry;

namespace Glab.C_Documentation
{
    public static class ViewportUtils
    {
        /// <summary>
        /// Captures a viewport from a named view in the Rhino document and returns it as a GImage with optional 3D rectangle cropping
        /// </summary>
        /// <param name="viewName">The name of the view to capture. If empty or null, captures the current active viewport state</param>
        /// <param name="displayMode">The display mode name to use for capturing (e.g. "Shaded", "Rendered", etc.)</param>
        /// <param name="zoomToCrop">If true and cropRectangle3d is provided, zooms the viewport to fit the crop rectangle before capturing</param>
        /// <param name="maxGImageSize">Maximum size for the output image, defaults to 1000</param>
        /// <param name="useGImageWidth">If true, uses width for scaling; otherwise uses height</param>
        /// <param name="cropRectangle3d">Optional 3D rectangle in world coordinates to crop the viewport to</param>
        /// <returns>A GImage containing the viewport capture</returns>
        public static GImage ViewportCaptureToGImage(
            string viewName,
            string displayMode = null,
            bool zoomToCrop = false,
            int maxGImageSize = 1000,
            bool useGImageWidth = true,
            Rectangle3d? cropRectangle3d = null)
        {
            try
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return null;

                RhinoView view;
                if (!string.IsNullOrEmpty(viewName))
                {
                    var namedView = doc.Views.Find(viewName, false);
                    view = namedView ?? doc.Views.ActiveView;
                    if (view == null) return null;
                }
                else
                {
                    view = doc.Views.ActiveView;
                    if (view == null) return null;
                }

                if (!string.IsNullOrEmpty(displayMode))
                {
                    var modes = DisplayModeDescription.GetDisplayModes();
                    foreach (var mode in modes)
                    {
                        if (mode.EnglishName.Equals(displayMode, StringComparison.OrdinalIgnoreCase) ||
                            mode.LocalName.Equals(displayMode, StringComparison.OrdinalIgnoreCase))
                        {
                            view.ActiveViewport.DisplayMode = mode;
                            break;
                        }
                    }
                }

                // Store original viewport state for restoration
                ViewInfo originalViewInfo = null;
                if (zoomToCrop && cropRectangle3d.HasValue)
                {
                    originalViewInfo = new ViewInfo(view.ActiveViewport);
                    ZoomViewportToRectangle(view.ActiveViewport, cropRectangle3d.Value);
                }

                view.Redraw();

                // Capture the viewport at current window size
                Bitmap bitmap = view.CaptureToBitmap();
                if (bitmap == null)
                {
                    // Restore original viewport if we modified it
                    if (originalViewInfo != null)
                    {
                        RestoreViewport(view.ActiveViewport, originalViewInfo);
                        view.Redraw();
                    }
                    return null;
                }

                // Always crop if cropRectangle3d is provided, regardless of zoom setting
                // This ensures the final image matches the exact crop rectangle bounds
                if (cropRectangle3d.HasValue)
                {
                    var viewportSize = view.ActiveViewport.Size;
                    var cropRect = Get2DCropRectangleFromRhino3D(
                        view.ActiveViewport,
                        cropRectangle3d.Value,
                        bitmap.Width,
                        bitmap.Height,
                        viewportSize);
                    if (cropRect.Width > 0 && cropRect.Height > 0)
                    {
                        bitmap = CropBitmap(bitmap, cropRect);
                    }
                }

                var isImage = ConvertBitmapToImage(bitmap);

                // Calculate final GImage size based on maxGImageSize and useGImageWidth
                int imageWidth = isImage.Width;
                int imageHeight = isImage.Height;
                
                double imageScaleFactor = useGImageWidth
                    ? (double)maxGImageSize / imageWidth
                    : (double)maxGImageSize / imageHeight;

                int finalWidth = (int)Math.Ceiling(imageWidth * imageScaleFactor);
                int finalHeight = (int)Math.Ceiling(imageHeight * imageScaleFactor);

                // Resize the captured image to the final GImage size
                isImage.Mutate(ctx => ctx.Resize(finalWidth, finalHeight));

                // Create the GImage with the final dimensions using the captured background
                var gImage = new GImage(
                    new SixLabors.ImageSharp.Rectangle(0, 0, finalWidth, finalHeight),
                    System.Drawing.Color.White)
                {
                    Image = isImage
                };

                bitmap.Dispose();

                // Restore original viewport if we modified it
                if (originalViewInfo != null)
                {
                    RestoreViewport(view.ActiveViewport, originalViewInfo);
                    view.Redraw();
                }

                return gImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing viewport: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Zooms the viewport to fit the specified 3D rectangle
        /// </summary>
        /// <param name="viewport">The viewport to zoom</param>
        /// <param name="rectangle3d">The 3D rectangle to zoom to</param>
        private static void ZoomViewportToRectangle(RhinoViewport viewport, Rectangle3d rectangle3d)
        {
            // Convert Rectangle3d to BoundingBox for ZoomBoundingBox method
            var bbox = new BoundingBox(rectangle3d.Corner(0), rectangle3d.Corner(2));
            
            // Add some padding to ensure the rectangle fits nicely in the viewport
            bbox.Inflate(Math.Max(bbox.Diagonal.Length * 0.1, 1.0));
            
            // Use ZoomBoundingBox to fit the rectangle in the viewport
            viewport.ZoomBoundingBox(bbox);
        }

        /// <summary>
        /// Restores the viewport to its original state
        /// </summary>
        /// <param name="viewport">The viewport to restore</param>
        /// <param name="originalViewInfo">The original viewport state</param>
        private static void RestoreViewport(RhinoViewport viewport, ViewInfo originalViewInfo)
        {
            // Restore the original viewport state
            viewport.SetCameraLocation(originalViewInfo.Viewport.CameraLocation, false);
            viewport.SetCameraDirection(originalViewInfo.Viewport.CameraDirection, false);
        }

        /// <summary>
        /// Converts a 3D rectangle in world coordinates to a 2D crop rectangle in bitmap coordinates
        /// </summary>
        /// <param name="viewport">The Rhino viewport</param>
        /// <param name="rectangle3d">The 3D rectangle in world coordinates</param>
        /// <param name="bitmapWidth">The width of the captured bitmap</param>
        /// <param name="bitmapHeight">The height of the captured bitmap</param>
        /// <param name="viewportSize">The size of the viewport client area</param>
        /// <returns>A Rectangle defining the crop area in bitmap coordinates</returns>
        private static System.Drawing.Rectangle Get2DCropRectangleFromRhino3D(RhinoViewport viewport, Rectangle3d rectangle3d, int bitmapWidth, int bitmapHeight, System.Drawing.Size viewportSize)
        {
            // Get the four corners of the 3D rectangle
            var corners = rectangle3d.ToPolyline();
            
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            // Convert each corner to viewport client coordinates and find bounding box
            foreach (var corner in corners)
            {
                Point2d clientPoint = viewport.WorldToClient(corner);
                
                minX = Math.Min(minX, clientPoint.X);
                minY = Math.Min(minY, clientPoint.Y);
                maxX = Math.Max(maxX, clientPoint.X);
                maxY = Math.Max(maxY, clientPoint.Y);
            }

            // Calculate scaling factors between viewport client coordinates and bitmap coordinates
            double scaleX = (double)bitmapWidth / viewportSize.Width;
            double scaleY = (double)bitmapHeight / viewportSize.Height;

            // Transform client coordinates to bitmap coordinates
            double bitmapMinX = minX * scaleX;
            double bitmapMinY = minY * scaleY;
            double bitmapMaxX = maxX * scaleX;
            double bitmapMaxY = maxY * scaleY;

            // Convert to integers and clamp to bitmap bounds
            int x = Math.Max(0, (int)Math.Floor(bitmapMinX));
            int y = Math.Max(0, (int)Math.Floor(bitmapMinY));
            int width = Math.Min(bitmapWidth - x, (int)Math.Ceiling(bitmapMaxX - bitmapMinX));
            int height = Math.Min(bitmapHeight - y, (int)Math.Ceiling(bitmapMaxY - bitmapMinY));

            // Ensure positive dimensions
            width = Math.Max(0, width);
            height = Math.Max(0, height);

            System.Diagnostics.Debug.WriteLine($"Viewport size: {viewportSize.Width}x{viewportSize.Height}");
            System.Diagnostics.Debug.WriteLine($"Bitmap size: {bitmapWidth}x{bitmapHeight}");
            System.Diagnostics.Debug.WriteLine($"Scale factors: {scaleX:F3}x{scaleY:F3}");
            System.Diagnostics.Debug.WriteLine($"Client coords: ({minX:F1},{minY:F1}) to ({maxX:F1},{maxY:F1})");
            System.Diagnostics.Debug.WriteLine($"Bitmap coords: ({bitmapMinX:F1},{bitmapMinY:F1}) to ({bitmapMaxX:F1},{bitmapMaxY:F1})");
            System.Diagnostics.Debug.WriteLine($"Crop rectangle: {x},{y} {width}x{height}");

            return new System.Drawing.Rectangle(x, y, width, height);
        }

        /// <summary>
        /// Crops a bitmap to the specified rectangle
        /// </summary>
        /// <param name="originalBitmap">The original bitmap to crop</param>
        /// <param name="cropRect">The rectangle defining the crop area</param>
        /// <returns>A new cropped bitmap</returns>
        private static Bitmap CropBitmap(Bitmap originalBitmap, System.Drawing.Rectangle cropRect)
        {
            // Validate crop rectangle
            if (cropRect.Width <= 0 || cropRect.Height <= 0)
                return originalBitmap;

            // Ensure crop rectangle is within bounds
            cropRect = System.Drawing.Rectangle.Intersect(cropRect, new System.Drawing.Rectangle(0, 0, originalBitmap.Width, originalBitmap.Height));
            
            if (cropRect.Width <= 0 || cropRect.Height <= 0)
                return originalBitmap;

            Bitmap croppedBitmap = new Bitmap(cropRect.Width, cropRect.Height);
            using (Graphics g = Graphics.FromImage(croppedBitmap))
            {
                g.DrawImage(originalBitmap, new System.Drawing.Rectangle(0, 0, cropRect.Width, cropRect.Height), cropRect, GraphicsUnit.Pixel);
            }
            
            // Dispose the original bitmap to free memory
            originalBitmap.Dispose();
            
            return croppedBitmap;
        }

        /// <summary>
        /// Converts a System.Drawing.Bitmap to SixLabors.ImageSharp.Image
        /// </summary>
        private static Image<Rgba32> ConvertBitmapToImage(Bitmap bitmap)
        {
            // Try to use the utility method if available
            if (typeof(ImageUtils).GetMethod("ConvertToISImage") != null)
            {
                return ImageUtils.ConvertToISImage(bitmap);
            }

            // Fallback implementation if utility method is not available
            using (var memoryStream = new System.IO.MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Position = 0;
                return SixLabors.ImageSharp.Image.Load<Rgba32>(memoryStream);
            }
        }

        /// <summary>
        /// Sets the current Rhino viewport to the specified named view
        /// </summary>
        /// <param name="viewName">The name of the view to set as current</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetCurrentView(string viewName)
        {
            try
            {
                // Get the active Rhino document
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return false;

                // Find the view by name in the NamedViews collection
                int namedViewIndex = doc.NamedViews.FindByName(viewName);
                if (namedViewIndex < 0)
                    return false;

                // Get the named view
                var namedView = doc.NamedViews[namedViewIndex];

                // Restore the named view to the current view
                if (!doc.NamedViews.Restore(namedViewIndex, doc.Views.ActiveView.ActiveViewport))
                    return false;

                // Get the updated view
                var view = doc.Views.ActiveView;

                // Set the found view as the active view
                doc.Views.ActiveView = view;

                // Ensure the view is up to date
                view.Redraw();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting current view: {ex.Message}");
                return false;
            }
        }
    }
}
