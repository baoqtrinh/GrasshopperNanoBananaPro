using Glab.Utilities;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using IS = SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using SD = System.Drawing;

namespace Glab.C_Documentation.DrawImg
{
    public class OverlapImages : GH_Component
    {
        public OverlapImages()
            : base("Overlap Images", "OverlapGImg",
                  "Overlap a base GImage with one or more overlay GImages",
                  "Glab", "Documentation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Base Image", "BI", "Base GImage to overlay onto", GH_ParamAccess.item);
            pManager.AddGenericParameter("Overlay Images", "OI", "GImage objects to overlay on the base image", GH_ParamAccess.list);
            pManager.AddIntegerParameter("X Positions", "X", "X coordinates for each overlay (list should match overlay count)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Y Positions", "Y", "Y coordinates for each overlay (list should match overlay count)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Opacities", "O", "Opacity values (0.0-1.0) for each overlay", GH_ParamAccess.list);
            pManager.AddNumberParameter("Scales", "S", "Scale factors for each overlay (1.0 = original size)", GH_ParamAccess.list);

            // Make the position, opacity, and scale inputs optional
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Combined Image", "CI", "The combined GImage result", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize input variables
            GImage baseImage = null;
            List<GImage> overlayImages = new List<GImage>();
            List<int> xPositions = new List<int>();
            List<int> yPositions = new List<int>();
            List<double> opacities = new List<double>();
            List<double> scales = new List<double>();

            // Get base image
            if (!DA.GetData(0, ref baseImage) || baseImage == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No base image provided");
                return;
            }

            // Get overlay images
            if (!DA.GetDataList(1, overlayImages) || overlayImages.Count == 0)
            {
                // If no overlays, just return the base image
                DA.SetData(0, new GH_ObjectWrapper(baseImage));
                return;
            }

            // Get optional position, opacity, and scale data
            DA.GetDataList(2, xPositions);
            DA.GetDataList(3, yPositions);
            DA.GetDataList(4, opacities);
            DA.GetDataList(5, scales);

            try
            {
                // Start with the base image
                GImage resultGImage = baseImage;

                // Loop through overlay images and apply them one by one
                for (int i = 0; i < overlayImages.Count; i++)
                {
                    // Get position, opacity, and scale for this overlay
                    int x = i < xPositions.Count ? xPositions[i] : 0;
                    int y = i < yPositions.Count ? yPositions[i] : 0;
                    float opacity = i < opacities.Count ? (float)opacities[i] : 1.0f;
                    float scale = i < scales.Count ? (float)scales[i] : 1.0f;

                    // Apply the overlay using the single image overlay method
                    resultGImage = ImageUtils.OverlapGImage(
                        resultGImage,
                        overlayImages[i],
                        (x, y),
                        opacity,
                        scale);

                    if (resultGImage == null)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error overlaying image at index {i}");
                        return;
                    }
                }

                // Output the combined GImage
                DA.SetData(0, new GH_ObjectWrapper(resultGImage));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error overlapping images: {ex.Message}");
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override SD.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("a1b2c3d4-5e6f-7a8b-9c0d-1e2f3a4b5c6d");
    }
}