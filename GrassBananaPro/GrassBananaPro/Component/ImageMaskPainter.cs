using System;
using System.Collections.Generic;
using SD = System.Drawing;
using IS = SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Glab.Utilities;

namespace Glab.C_Documentation.DrawImg
{
    /// <summary>
    /// Component for painting masks on images using a WPF brush tool
    /// </summary>
    public class ImageMaskPainter : GH_Component
    {
        private GImage _inputImage;
        private GImage _maskedImage;
        private IS.Image<Rgba32> _maskLayer;
        private string _message = "No image loaded";
        private SD.Color _initialMaskColor = SD.Color.FromArgb(255, 255, 0, 0);
        private int _initialBrushSize = 20;

        /// <summary>
        /// Initializes a new instance of the ImageMaskPainter class.
        /// </summary>
        public ImageMaskPainter()
          : base("Image Mask Painter", "MaskPaint",
              "Paint masks on images using a WPF brush tool. Double-click to open editor.",
              "Glab", "Documentation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("GImage", "I", "Input image to paint mask on", GH_ParamAccess.item);
            pManager.AddColourParameter("Mask Color", "C", "Color for the mask brush", GH_ParamAccess.item, SD.Color.FromArgb(255, 255, 0, 0)); // Fully opaque red
            pManager.AddIntegerParameter("Brush Size", "S", "Brush size in pixels", GH_ParamAccess.item, 20);
            pManager.AddBooleanParameter("Reset", "R", "Reset the mask to empty", GH_ParamAccess.item, false);

            // Make inputs optional
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Masked GImage", "M", "Image with painted mask overlay", GH_ParamAccess.item);
            pManager.AddGenericParameter("Mask Only", "MO", "Just the mask layer as GImage", GH_ParamAccess.item);
            pManager.AddTextParameter("Info", "I", "Information about the mask", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Goo imageGoo = null;
            SD.Color maskColor = SD.Color.FromArgb(255, 255, 0, 0); // Default fully opaque
            int brushSize = 20;
            bool reset = false;

            // Get input data
            if (!DA.GetData(0, ref imageGoo))
            {
                _message = "No image provided";
                DA.SetData(2, _message);
                return;
            }

            DA.GetData(1, ref maskColor);
            DA.GetData(2, ref brushSize);
            DA.GetData(3, ref reset);

            // Force mask color to be fully opaque (alpha = 255)
            maskColor = SD.Color.FromArgb(255, maskColor.R, maskColor.G, maskColor.B);

            // Store initial settings for WPF window override
            _initialMaskColor = maskColor;
            _initialBrushSize = brushSize;

            // Extract GImage from input
            GImage inputImage = null;
            if (imageGoo is GH_ObjectWrapper wrapper && wrapper.Value is GImage gImg)
            {
                inputImage = gImg;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input must be a GImage object");
                _message = "Invalid input";
                DA.SetData(2, _message);
                return;
            }

            if (inputImage?.Image == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GImage has no valid image data");
                _message = "Invalid image data";
                DA.SetData(2, _message);
                return;
            }

            // Store input image
            _inputImage = inputImage;

            // Reset mask if requested or if image changed
            if (reset || _maskLayer == null || 
                _maskLayer.Width != _inputImage.Image.Width || 
                _maskLayer.Height != _inputImage.Image.Height)
            {
                _maskLayer?.Dispose();
                _maskLayer = new IS.Image<Rgba32>(_inputImage.Image.Width, _inputImage.Image.Height);
                
                // Fill with transparent
                for (int y = 0; y < _maskLayer.Height; y++)
                {
                    for (int x = 0; x < _maskLayer.Width; x++)
                    {
                        _maskLayer[x, y] = new Rgba32(0, 0, 0, 0);
                    }
                }
            }

            // Create masked image by overlaying mask on original
            _maskedImage = CreateMaskedImage(_inputImage, _maskLayer);

            // Create mask-only image
            var maskOnlyImage = new GImage(
                new IS.Rectangle(0, 0, _maskLayer.Width, _maskLayer.Height), 
                SD.Color.Transparent)
            {
                Image = _maskLayer.Clone()
            };

            // Count non-transparent pixels in mask
            int maskedPixels = CountMaskedPixels(_maskLayer);
            double maskPercentage = (maskedPixels * 100.0) / (_maskLayer.Width * _maskLayer.Height);

            _message = $"Image: {_inputImage.Image.Width}x{_inputImage.Image.Height}, Masked: {maskedPixels:N0} pixels ({maskPercentage:F2}%)";
            Message = $"{_inputImage.Image.Width}x{_inputImage.Image.Height}";

            // Output
            DA.SetData(0, new GH_ObjectWrapper(_maskedImage));
            DA.SetData(1, new GH_ObjectWrapper(maskOnlyImage));
            DA.SetData(2, _message);
        }

        /// <summary>
        /// Creates a new image with mask overlay
        /// </summary>
        private GImage CreateMaskedImage(GImage original, IS.Image<Rgba32> mask)
        {
            var result = original.Image.Clone();
            
            // Overlay mask on original image
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    var maskPixel = mask[x, y];
                    if (maskPixel.A > 0)
                    {
                        var originalPixel = result[x, y];
                        
                        // Alpha blend
                        float alpha = maskPixel.A / 255f;
                        byte r = (byte)(maskPixel.R * alpha + originalPixel.R * (1 - alpha));
                        byte g = (byte)(maskPixel.G * alpha + originalPixel.G * (1 - alpha));
                        byte b = (byte)(maskPixel.B * alpha + originalPixel.B * (1 - alpha));
                        
                        result[x, y] = new Rgba32(r, g, b, 255);
                    }
                }
            }

            return new GImage(
                new IS.Rectangle(0, 0, result.Width, result.Height),
                SD.Color.White)
            {
                Image = result
            };
        }

        /// <summary>
        /// Counts non-transparent pixels in mask
        /// </summary>
        private int CountMaskedPixels(IS.Image<Rgba32> mask)
        {
            int count = 0;
            for (int y = 0; y < mask.Height; y++)
            {
                for (int x = 0; x < mask.Width; x++)
                {
                    if (mask[x, y].A > 0)
                        count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Updates the mask layer (called from WPF window)
        /// </summary>
        public void UpdateMask(IS.Image<Rgba32> newMask)
        {
            _maskLayer?.Dispose();
            _maskLayer = newMask;
            ExpireSolution(true);
        }

        /// <summary>
        /// Gets the current input image
        /// </summary>
        public GImage GetInputImage()
        {
            return _inputImage;
        }

        /// <summary>
        /// Gets the current mask layer
        /// </summary>
        public IS.Image<Rgba32> GetMaskLayer()
        {
            return _maskLayer;
        }

        /// <summary>
        /// Creates custom attributes for double-click handling
        /// </summary>
        public override void CreateAttributes()
        {
            m_attributes = new ImageMaskPainterAttributes(this);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Gets the unique ID for this component.
        /// </summary>
        public override Guid ComponentGuid => new Guid("F8A7B6C5-D4E3-2109-8765-43210FEDCBA9");

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override SD.Bitmap Icon => null;

        /// <summary>
        /// Custom attributes class for handling double-click
        /// </summary>
        private class ImageMaskPainterAttributes : GH_ComponentAttributes
        {
            private readonly ImageMaskPainter _owner;

            public ImageMaskPainterAttributes(ImageMaskPainter owner) : base(owner)
            {
                _owner = owner;
            }

            public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (_owner._inputImage?.Image == null)
                {
                    System.Windows.MessageBox.Show(
                        "Please provide an input image first.",
                        "No Image",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return GH_ObjectResponse.Handled;
                }

                // Open WPF window for mask painting
                var window = new MaskPainterWindow(_owner);
                window.ShowDialog();

                return GH_ObjectResponse.Handled;
            }
        }

        public SD.Color GetInitialMaskColor() => _initialMaskColor;
        public int GetInitialBrushSize() => _initialBrushSize;
    }
}
