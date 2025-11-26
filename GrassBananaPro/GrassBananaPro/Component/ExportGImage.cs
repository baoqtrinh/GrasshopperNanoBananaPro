using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Glab.C_Documentation;
using Glab.Utilities;
using Grasshopper.Kernel;
using Rhino.FileIO;

namespace Glab.C_Documentation
{
    public class ExportGImage : GH_Component
    {
        private List<string> _savedFilePaths = new List<string>();

        public ExportGImage()
            : base("Export GImage", "ExportGImage",
                  "Export a list of GImages to a specific folder",
                  "Glab", "Documentation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("GImage*", "GI", "List of GImages to export", GH_ParamAccess.list);
            pManager.AddTextParameter("Folder Path*", "FP", "Folder path to save the images", GH_ParamAccess.item);
            pManager.AddTextParameter("File Name*", "FN", "List of file names for the exported images (without extension)", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Export", "E", "Set to true to trigger export operation", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Overwrite", "O", "Set to true to overwrite existing files", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Message", "M", "Export status message", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var gImages = new List<GImage>();
            string folderPath = string.Empty;
            var fileNames = new List<string>();
            bool export = false;
            bool overwrite = false;

            if (!DA.GetDataList(0, gImages) || gImages.Count == 0)
            {
                DA.SetData(0, "No GImages provided.");
                return;
            }
            if (!DA.GetData(1, ref folderPath) || string.IsNullOrEmpty(folderPath))
            {
                DA.SetData(0, "No folder path provided.");
                return;
            }
            if (!DA.GetDataList(2, fileNames) || fileNames.Count == 0)
            {
                DA.SetData(0, "No file names provided.");
                return;
            }
            DA.GetData(3, ref export);
            DA.GetData(4, ref overwrite);

            if (gImages.Count != fileNames.Count)
            {
                DA.SetData(0, "The number of file names must match the number of GImages.");
                return;
            }

            if (!export)
            {
                DA.SetData(0, "Set Export to True to save the images.");
                return;
            }

            _savedFilePaths.Clear();
            var messages = new List<string>();

            for (int i = 0; i < gImages.Count; i++)
            {
                var gImage = gImages[i];
                var fileName = fileNames[i];

                if (gImage == null)
                {
                    messages.Add($"GImage at index {i} is null and was skipped.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"Image_{i + 1}";
                }

                string fullFilePath = Path.Combine(folderPath, $"{fileName}.png");

                // Overwrite logic
                if (File.Exists(fullFilePath) && !overwrite)
                {
                    messages.Add($"File '{fullFilePath}' already exists and was not overwritten.");
                    continue;
                }

                bool imageSaved = ImageUtils.SaveImage(gImage.Image, fullFilePath);

                if (imageSaved)
                {
                    _savedFilePaths.Add(fullFilePath);
                    messages.Add($"Image '{fileName}.png' exported successfully.");
                }
                else
                {
                    messages.Add($"Failed to save image at index {i} to path: {fullFilePath}");
                }
            }

            DA.SetData(0, string.Join(Environment.NewLine, messages));
        }

        // Add right-click menu for "Open Folder"
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            var openFolderMenuItem = new ToolStripMenuItem("Open Folder", null, OpenFolderMenuItem_Click);
            menu.Items.Add(openFolderMenuItem);
        }

        private void OpenFolderMenuItem_Click(object sender, EventArgs e)
        {
            if (_savedFilePaths == null || _savedFilePaths.Count == 0)
            {
                MessageBox.Show("No files have been saved yet.", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Open the folder and select the first file
            var firstFilePath = _savedFilePaths[0];
            var directoryPath = Path.GetDirectoryName(firstFilePath);

            if (Directory.Exists(directoryPath))
            {
                if (File.Exists(firstFilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{firstFilePath}\"");
                }
                else
                {
                    Process.Start("explorer.exe", directoryPath);
                }
            }
            else
            {
                MessageBox.Show("The directory does not exist.", "Directory Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("e3f4c5b6-7a8d-9e0f-1b2c-3d4e5f6a7b8c");
    }
}
