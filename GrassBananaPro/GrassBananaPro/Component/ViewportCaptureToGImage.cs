using System;
using System.Drawing;
using System.Windows.Forms;
using Glab.C_Documentation;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using System.Linq;
using Rhino.Display;
using Rhino.DocObjects.Tables;
using Rhino.Geometry;
using SixLabors.ImageSharp;
using System.Threading.Tasks;

namespace GlabMassingDatamanagement.C_Documentation.Dataset
{
    public class ViewportCaptureToGImage : GH_Component
    {
        private bool _needsRefresh = true; // Flag to track if we need to refresh before capture

        /// <summary>
        /// Initializes a new instance of the ViewportCaptureToGImage class.
        /// </summary>
        public ViewportCaptureToGImage()
          : base("Viewport Capture To GImage", "ViewCapture",
              "Captures a viewport and returns it as a GImage. If no view name is provided, captures the current active viewport. Supports 3D rectangle cropping with optional zoom-to-crop for maximum detail.",
              "Glab", "Documentation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("View Name", "V", "Name of the Rhino view to capture. Leave blank to capture the current viewport.", GH_ParamAccess.item);
            pManager.AddTextParameter("Display Mode", "D", "Display mode to use for the capture (e.g., 'Shaded', 'Rendered', 'Wireframe')", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Zoom to Crop", "Z", "If true and crop rectangles are provided, zooms the viewport to fit each crop rectangle before capturing for maximum detail", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Max GImage Size", "GS", "Maximum size for the output GImage (default: 1000)", GH_ParamAccess.item, 1000);
            pManager.AddBooleanParameter("Use GImage Width", "GW", "If true, uses GImage width for scaling; otherwise uses height (default: true)", GH_ParamAccess.item, true);
            pManager.AddGeometryParameter("Crop Rectangles", "C", "List of curves to define crop rectangles in world coordinates", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Force Refresh", "FR", "Force refresh of other components before capture (default: true)", GH_ParamAccess.item, true);

            // Make all inputs optional
            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GImage", "G", "Captured viewport as a list of GImages", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string viewName = string.Empty;
            string displayMode = string.Empty;
            bool zoomToCrop = false;
            int maxGImageSize = 1000;
            bool useGImageWidth = true;
            bool forceRefresh = true;
            var cropCurves = new System.Collections.Generic.List<Curve>();

            DA.GetData(0, ref viewName);
            DA.GetData(1, ref displayMode);
            DA.GetData(2, ref zoomToCrop);
            DA.GetData(3, ref maxGImageSize);
            DA.GetData(4, ref useGImageWidth);
            DA.GetDataList(5, cropCurves);
            DA.GetData(6, ref forceRefresh);

            // If force refresh is enabled and this is the first run, expire and reschedule
            if (forceRefresh && _needsRefresh)
            {
                _needsRefresh = false;
                
                // Schedule a solution immediately to allow other components to update
                OnPingDocument().ScheduleSolution(1, _ =>
                {
                    ExpireSolution(true);
                });
                
                // Return empty for this iteration
                DA.SetDataList(0, new System.Collections.Generic.List<GImage>());
                return;
            }

            // Reset the refresh flag for next run
            _needsRefresh = true;

            if (maxGImageSize <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Max GImage size must be greater than zero.");
                return;
            }

            // If zoom to crop is enabled but no crop curves provided, show warning
            if (zoomToCrop && cropCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Zoom to crop is enabled but no crop rectangles provided. Capturing entire viewport instead.");
            }

            // Set the view if provided
            if (!string.IsNullOrEmpty(viewName))
            {
                bool success = ViewportUtils.SetCurrentView(viewName);
                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to set view '{viewName}'. Using current active view instead.");
                }
            }

            RhinoView activeView = RhinoDoc.ActiveDoc.Views.ActiveView;
            if (activeView == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No active view available to capture.");
                return;
            }

            // Force a redraw to ensure geometry is up to date
            if (forceRefresh)
            {
                RhinoDoc.ActiveDoc.Views.Redraw();
            }

            var gImages = new System.Collections.Generic.List<GImage>();

            // If no crop curves provided, capture the entire viewport
            if (cropCurves.Count == 0)
            {
                // Simple capture without crop rectangle changes
                string viewToCapture = string.IsNullOrEmpty(viewName) ? activeView.ActiveViewport.Name : viewName;
                var img = ViewportUtils.ViewportCaptureToGImage(
                    viewToCapture, 
                    displayMode, 
                    zoomToCrop,
                    maxGImageSize, 
                    useGImageWidth, 
                    null);
                if (img != null)
                    gImages.Add(img);
            }
            else
            {
                // Process each crop curve
                foreach (var curve in cropCurves)
                {
                    Rectangle3d? cropRectangle3d = null;
                    if (curve != null)
                    {
                        BoundingBox bbox = curve.GetBoundingBox(true);
                        if (bbox.IsValid)
                        {
                            Point3d min = new Point3d(bbox.Min.X, bbox.Min.Y, 0);
                            Point3d max = new Point3d(bbox.Max.X, bbox.Max.Y, 0);
                            cropRectangle3d = new Rectangle3d(Plane.WorldXY,
                                new Interval(min.X, max.X),
                                new Interval(min.Y, max.Y));
                        }
                    }

                    // Capture with crop rectangle - let ViewportUtils handle the zooming
                    string viewToCapture = string.IsNullOrEmpty(viewName) ? activeView.ActiveViewport.Name : viewName;
                    var img = ViewportUtils.ViewportCaptureToGImage(
                        viewToCapture, 
                        displayMode, 
                        zoomToCrop,
                        maxGImageSize, 
                        useGImageWidth, 
                        cropRectangle3d);
                    if (img != null)
                        gImages.Add(img);
                }
            }

            DA.SetDataList(0, gImages);
        }

        /// <summary>
        /// Zooms the viewport to fit the specified 3D rectangle
        /// </summary>
        /// <param name="viewport">The viewport to zoom</param>
        /// <param name="rectangle3d">The 3D rectangle to zoom to</param>
        private void ZoomViewportToRectangle(RhinoViewport viewport, Rectangle3d rectangle3d)
        {
            var bbox = new BoundingBox(rectangle3d.Corner(0), rectangle3d.Corner(2));
            bbox.Inflate(Math.Max(bbox.Diagonal.Length * 0.1, 1.0));
            viewport.ZoomBoundingBox(bbox);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon => null;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("5C937EE1-AAA4-42C1-B8D0-2FB36C28D3A7");
    }
}
