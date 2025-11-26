using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System.IO;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using SD = System.Drawing;
using IS = SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Glab.Utilities;

namespace Glab.C_Documentation.Preview
{
    public class PreviewImage : GH_Component
    {
        private SD.Image _originalImage;
        private SD.Image _previewImage;
        private IS.Image<Rgba32> _isImage;
        private string _filePath;
        private string _message = "No image loaded";
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the PreviewImage class.
        /// </summary>
        public PreviewImage()
          : base("Preview Image", "ImgPreview",
              "Previews an image from a file path or GImage object",
              "Glab", "Documentation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "F", "Path to the image file", GH_ParamAccess.item);
            pManager.AddGenericParameter("GImage", "G", "GImage or ImageSharp Image to preview", GH_ParamAccess.item);

            // Both inputs are optional, but at least one should be provided
            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GImage", "G", "GImage object", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = null;
            IGH_Goo imageGoo = null;

            // Dispose previous images
            _originalImage?.Dispose();
            _previewImage?.Dispose();
            _isImage?.Dispose();
            _originalImage = null;
            _previewImage = null;
            _isImage = null;

            // Try to get GImage input first
            bool hasGImage = DA.GetData(1, ref imageGoo);

            if (hasGImage)
            {
                if (TryGetImageFromGoo(imageGoo, out GImage gImage))
                {
                    _isImage = gImage.Image;

                    // Resize the image to fit within 600x600 pixels for preview
                    var resizedImage = ResizeImage(_isImage, 600, 600);

                    // Convert to SD.Image for preview
                    _previewImage = ImageUtils.ConvertToSDImage(resizedImage);

                    _message = $"({_isImage.Width}x{_isImage.Height}) GImage";
                    DA.SetData(0, new GH_ObjectWrapper(gImage));
                    Message = _message;
                    return;
                }
            }

            // If no valid GImage, try file path
            if (DA.GetData(0, ref filePath))
            {
                // Load the image from the file path
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        // Load the original SD.Image
                        _originalImage = SD.Image.FromFile(filePath);
                        _filePath = filePath;
                        _message = $"({_originalImage.Width}x{_originalImage.Height}) {_originalImage.PixelFormat}";

                        // Convert to ImageSharp Image
                        _isImage = ImageUtils.ConvertToISImage(_originalImage);

                        // Resize the image to fit within 600x600 pixels for preview
                        var resizedImage = ResizeImage(_isImage, 600, 600);

                        // Update the _previewImage field with the resized image
                        _previewImage = ImageUtils.ConvertToSDImage(resizedImage);

                        // Create a GImage object
                        var gImage = new GImage(new IS.Rectangle(0, 0, _isImage.Width, _isImage.Height), SD.Color.White)
                        {
                            Image = _isImage
                        };

                        // Output the GImage object
                        DA.SetData(0, new GH_ObjectWrapper(gImage));
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to load image: {ex.Message}");
                        _originalImage = null;
                        _previewImage = null;
                        _isImage = null;
                        _message = "Error loading image";
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid file path.");
                    _message = "Invalid file path";
                }
            }
            else if (!hasGImage)
            {
                // No input provided
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Please provide either a file path or a GImage object.");
                _message = "No input provided";
            }

            // Update component message
            Message = _message;
        }

        /// <summary>
        /// Tries to extract a GImage from various input types
        /// </summary>
        private bool TryGetImageFromGoo(IGH_Goo goo, out GImage gImage)
        {
            gImage = null;

            // Check if it's a GImage
            if (goo is GH_ObjectWrapper wrapper)
            {
                if (wrapper.Value is GImage gImageValue && gImageValue.Image != null)
                {
                    // Create a copy to avoid modifying the original
                    gImage = new GImage(gImageValue.ISRectangle, gImageValue.Color)
                    {
                        Image = gImageValue.Image.Clone()
                    };
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resizes an ImageSharp image to fit within the specified maximum dimensions while maintaining aspect ratio.
        /// </summary>
        private IS.Image<Rgba32> ResizeImage(IS.Image<Rgba32> image, int maxWidth, int maxHeight)
        {
            // Calculate the scaling factor to maintain aspect ratio
            double scale = Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height);

            // Calculate the new dimensions
            int newWidth = (int)(image.Width * scale);
            int newHeight = (int)(image.Height * scale);

            // Resize the image using the calculated dimensions
            var options = new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new IS.Size(newWidth, newHeight)
            };

            return image.Clone(ctx => ctx.Resize(options));
        }

        public override GH_Exposure Exposure => GH_Exposure.quinary;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("985411ED-DCEE-454C-B3CB-BE6FE3715EEF"); }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override SD.Bitmap Icon
        {
            get { return null; }
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            if (_originalImage != null)
            {
                Menu_AppendItem(menu, "Save Image As...", OnSaveImage);
            }
        }

        private void OnSaveImage(object sender, EventArgs e)
        {
            if (_originalImage == null) return;

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "JPEG Image|*.jpg|PNG Image|*.png|Bitmap Image|*.bmp";
                dialog.Title = "Save Image As";

                if (!string.IsNullOrEmpty(_filePath))
                    dialog.FileName = Path.GetFileName(_filePath);
                else
                    dialog.FileName = "image.png";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string extension = Path.GetExtension(dialog.FileName).ToLower();
                        switch (extension)
                        {
                            case ".jpg":
                            case ".jpeg":
                                _originalImage.Save(dialog.FileName, SD.Imaging.ImageFormat.Jpeg);
                                break;
                            case ".png":
                                _originalImage.Save(dialog.FileName, SD.Imaging.ImageFormat.Png);
                                break;
                            case ".bmp":
                                _originalImage.Save(dialog.FileName, SD.Imaging.ImageFormat.Bmp);
                                break;
                            default:
                                _originalImage.Save(dialog.FileName);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error saving image: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// Creates custom attributes for the component.
        /// </summary>
        public override void CreateAttributes()
        {
            m_attributes = new PreviewImageAttributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            if (!string.IsNullOrEmpty(_filePath))
                writer.SetString("ImagePath", _filePath);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("ImagePath"))
            {
                _filePath = reader.GetString("ImagePath");
                if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
                {
                    try
                    {
                        _originalImage?.Dispose();
                        _previewImage?.Dispose();
                        _isImage?.Dispose();

                        _originalImage = SD.Image.FromFile(_filePath);
                        _message = $"({_originalImage.Width}x{_originalImage.Height}) {_originalImage.PixelFormat}";

                        // Convert to ImageSharp Image
                        _isImage = ImageUtils.ConvertToISImage(_originalImage);

                        // Resize the image to fit within 600x600 pixels for preview
                        var resizedImage = ResizeImage(_isImage, 600, 600);

                        // Update the _previewImage field with the resized image
                        _previewImage = ImageUtils.ConvertToSDImage(resizedImage);
                    }
                    catch
                    {
                        _originalImage = null;
                        _previewImage = null;
                        _isImage = null;
                        _message = "Error loading image";
                    }
                }
            }
            return base.Read(reader);
        }

        // Add a finalizer to clean up resources
        ~PreviewImage()
        {
            CleanupResources();
        }

        // Method to clean up resources
        private void CleanupResources()
        {
            if (!_disposed)
            {
                // Dispose managed resources
                _originalImage?.Dispose();
                _previewImage?.Dispose();
                _isImage?.Dispose();

                _disposed = true;
            }
        }

        // Override ClearData to ensure resources are cleaned up
        public override void ClearData()
        {
            CleanupResources();
            base.ClearData();
        }

        private class PreviewImageAttributes : GH_ComponentAttributes
        {
            private readonly PreviewImage _owner;
            private SD.Rectangle _imageBounds;
            private static SD.Image _checkerboardImage;

            public PreviewImageAttributes(PreviewImage owner) : base(owner)
            {
                _owner = owner;
                if (_checkerboardImage == null)
                {
                    _checkerboardImage = CreateCheckerboardImage(10, 10, SD.Color.LightGray, SD.Color.Gray);
                }
            }

            protected override void Layout()
            {
                base.Layout();

                // Get the standard bounds from base layout
                SD.Rectangle baseBounds = GH_Convert.ToRectangle(Bounds);

                // Set default image dimensions
                int width = 200;  // Default width
                int height = 150; // Default height

                if (_owner._previewImage != null)
                {
                    try
                    {
                        // Use actual image dimensions, but limit size
                        width = _owner._previewImage.Width;
                        height = _owner._previewImage.Height;

                        // Scale down if too large
                        if (width > 300)
                        {
                            float scale = 300f / width;
                            width = 300;
                            height = (int)(height * scale);
                        }
                        if (height > 200)
                        {
                            float scale = 200f / height;
                            height = 200;
                            width = (int)(width * scale);
                        }

                        // Ensure minimum size
                        if (width < 100) width = 100;
                        if (height < 100) height = 100;
                    }
                    catch (Exception ex)
                    {
                        // Handle any exceptions that occur while accessing image properties
                        _owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error accessing image properties: {ex.Message}");
                    }
                }

                // Adjust component bounds to fit the image
                int componentWidth = Math.Max(baseBounds.Width, width);

                // Create new bounds that include space for the image
                SD.Rectangle newBounds = new SD.Rectangle(
                    baseBounds.X,
                    baseBounds.Y,
                    componentWidth,
                    baseBounds.Height + height);

                // Set the image area bounds
                _imageBounds = new SD.Rectangle(
                    newBounds.X,
                    baseBounds.Bottom,
                    componentWidth,
                    height);

                // Update the component bounds
                Bounds = newBounds;

                // Adjust the output parameter position
                var outputParam = _owner.Params.Output[0];
                var outputBounds = outputParam.Attributes.Bounds;
                outputBounds.X = newBounds.Right - outputBounds.Width;
                outputParam.Attributes.Bounds = outputBounds;
            }

            protected override void Render(GH_Canvas canvas, SD.Graphics graphics, GH_CanvasChannel channel)
            {
                // First render the standard component parts
                base.Render(canvas, graphics, channel);

                if (channel == GH_CanvasChannel.Objects)
                {
                    // Draw a frame for the image area
                    GH_Capsule capsule = GH_Capsule.CreateCapsule(_imageBounds, GH_Palette.Normal, 0, 0);
                    capsule.Render(graphics, Selected, Owner.Locked, false);
                    capsule.Dispose();

                    // Draw the checkerboard background
                    DrawCheckerboardBackground(graphics, _imageBounds);

                    // Draw the image if available and valid
                    if (_owner._previewImage != null && IsValidImage(_owner._previewImage))
                    {
                        // Draw the image without padding
                        graphics.DrawImage(_owner._previewImage, _imageBounds);
                    }
                    else
                    {
                        // Draw a message if no image
                        SD.StringFormat format = new SD.StringFormat
                        {
                            Alignment = SD.StringAlignment.Center,
                            LineAlignment = SD.StringAlignment.Center
                        };

                        graphics.DrawString("Provide an input",
                            GH_FontServer.Standard, SD.Brushes.Black, _imageBounds, format);
                    }
                }
            }

            private bool IsValidImage(SD.Image image)
            {
                try
                {
                    // Access properties to ensure the image is valid
                    var width = image.Width;
                    var height = image.Height;
                    return width > 0 && height > 0;
                }
                catch
                {
                    return false;
                }
            }


            private void DrawCheckerboardBackground(SD.Graphics graphics, SD.Rectangle bounds)
            {
                if (_checkerboardImage != null)
                {
                    using (var textureBrush = new SD.TextureBrush(_checkerboardImage))
                    {
                        graphics.FillRectangle(textureBrush, bounds);
                    }
                }
            }

            private static SD.Image CreateCheckerboardImage(int cellSize, int numCells, SD.Color lightColor, SD.Color darkColor)
            {
                int size = cellSize * numCells;
                var bitmap = new SD.Bitmap(size, size);
                using (var graphics = SD.Graphics.FromImage(bitmap))
                {
                    for (int y = 0; y < numCells; y++)
                    {
                        for (int x = 0; x < numCells; x++)
                        {
                            bool isLightCell = (x + y) % 2 == 0;
                            SD.Brush brush = isLightCell ? new SD.SolidBrush(lightColor) : new SD.SolidBrush(darkColor);
                            graphics.FillRectangle(brush, x * cellSize, y * cellSize, cellSize, cellSize);
                        }
                    }
                }
                return bitmap;
            }

            public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                // Convert PointF to Point by casting to int
                SD.Point location = new SD.Point((int)e.CanvasLocation.X, (int)e.CanvasLocation.Y);

                // We've removed the image selection functionality
                // Just pass the double-click to the base implementation
                return base.RespondToMouseDoubleClick(sender, e);
            }
        }
    }
}
