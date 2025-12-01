using System;
using System.Collections.Generic;
using SD = System.Drawing;
using IS = SixLabors.ImageSharp;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Glab.Utilities;

namespace Glab.C_Documentation.DrawImg
{
    public class DrawImage : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DrawShape class.
        /// </summary>
        public DrawImage()
          : base("Draw Image", "DrawImage",
              "Draws filled curves with colored borders on an image",
              "Glab", "Documentation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Frame Curve", "F", "The curve defining the frame/boundary", GH_ParamAccess.item);
            pManager.AddColourParameter("Frame Fill Color", "FFC", "The fill color for the frame (optional)", GH_ParamAccess.item, SD.Color.Transparent);
            pManager.AddCurveParameter("Curves", "C", "The curves to draw and fill (optional)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Line Setting", "LS", "Line setting for border (or use Border Color)", GH_ParamAccess.item);
            pManager.AddColourParameter("Fill Color", "F", "The fill color for the regions made from the curves", GH_ParamAccess.item, SD.Color.Transparent);
            pManager.AddNumberParameter("Max Size", "S", "Maximum size (height or width) in pixels", GH_ParamAccess.item, 1000);
            pManager.AddBooleanParameter("Use Max Width", "W", "If true, scales based on width; if false, scales based on height", GH_ParamAccess.item, true);

            // Make the curves, line setting, and fill color inputs optional
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GImage", "G", "The output image with drawn curves", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Declare variables
            Curve frameCurve = null;
            SD.Color frameFillColor = SD.Color.Transparent;
            List<Curve> curves = new List<Curve>();
            object lineSettingObj = null;
            SD.Color fillColor = SD.Color.White;
            double maxSize = 1000;
            bool useWidth = true;

            // Get input data
            if (!DA.GetData(0, ref frameCurve)) return;
            DA.GetData(1, ref frameFillColor);

            // Try to get curves, but continue if not provided
            DA.GetDataList(2, curves);

            DA.GetData(3, ref lineSettingObj);
            DA.GetData(4, ref fillColor);
            DA.GetData(5, ref maxSize);
            DA.GetData(6, ref useWidth);

            // Validate frame curve
            if (frameCurve == null || !frameCurve.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid frame curve.");
                return;
            }

            // Validate max size
            if (maxSize <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Max size must be positive.");
                return;
            }

            // Parse LineSetting from input
            LineSetting lineSetting = null;
            if (lineSettingObj != null)
            {
                if (lineSettingObj is LineSetting ls)
                {
                    lineSetting = ls;
                }
                else if (lineSettingObj is GH_ObjectWrapper wrapper &&
                         wrapper.Value is LineSetting wrappedLS)
                {
                    lineSetting = wrappedLS;
                }
                else if (lineSettingObj is SD.Color || lineSettingObj is GH_Colour)
                {
                    // Handle the case where a color is provided instead of LineSetting
                    SD.Color borderColor = SD.Color.Black;

                    if (lineSettingObj is SD.Color color)
                    {
                        borderColor = color;
                    }
                    else if (lineSettingObj is GH_Colour ghColor)
                    {
                        ghColor.Value.ToArgb(); // Extract color from Grasshopper type
                        borderColor = ghColor.Value;
                    }

                    // Create a LineSetting with the color and default values
                    var borderColorIS = ColorUtils.ConvertColorSDtoIS(borderColor);
                    lineSetting = new LineSetting(2, null, borderColorIS);
                }
            }

            // If still null, create a default LineSetting with black color
            if (lineSetting == null)
            {
                lineSetting = new LineSetting(2, null, IS.Color.Black);
            }

            // Convert fill colors to SixLabors.ImageSharp.Color
            var fillColorIS = ColorUtils.ConvertColorSDtoIS(fillColor);
            var frameFillColorIS = ColorUtils.ConvertColorSDtoIS(frameFillColor);

            // Use the ImageUtils method to draw the curves with fill
            GImage result = ImageUtils.DrawCurvesWithFill(frameCurve, curves, lineSetting, fillColorIS, (int)maxSize, useWidth, frameFillColorIS);

            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to create image with curves.");
                return;
            }

            // Output the resulting GImage
            DA.SetData(0, new GH_ObjectWrapper(result));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override SD.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("EB191314-A7CC-4D6A-AEB6-0EEC40AF1117"); }
        }
    }
}