using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Glab.Utilities
{
    public class CustomTextParameter : Param_String
    {
        private readonly string parameterType;

        // Custom constructor with parameter type
        public CustomTextParameter(string type)
        {
            parameterType = type;
        }
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            // Add a separator for better visual organization
            menu.Items.Add(new ToolStripSeparator());

            // Create menu item with more descriptive text and image
            var extractMenuItem = new ToolStripMenuItem
            {
                Text = "Extract to Value List",
                ToolTipText = "Creates a dropdown value list based on parameter type",
                Name = "Extract Value List"
            };
            extractMenuItem.Click += ExtractParameter;

            menu.Items.Add(extractMenuItem);
        }

        // Modify ExtractParameter to use parameterType
        private void ExtractParameter(object sender, EventArgs e)
        {
            var document = OnPingDocument();
            if (document == null) return;

            var valueList = new GH_ValueList
            {
                ListMode = GH_ValueListMode.DropDown
            };

            valueList.ListItems.Clear();

            var items = parameterType switch
            {
                "FileName" => GetFileNames(),

                "JustificationType" => GetJustificationTypes(),
                "Regulation" => GetRegulation(),

                "TemplateType" => GetTemplateType(),
                "GDocObjType" => GetGDocObjType(),

                "ImageSize" => GetImageSizes(),
                "MediaResolution" => GetMediaResolutions(),
                "AspectRatio" => GetAspectRatios(),

                _ => new List<string>()
            };

            // Add new items
            foreach (var item in items)
            {
                valueList.ListItems.Add(new GH_ValueListItem(item, $"\"{item}\""));
            }

            // Ensure attributes are created
            if (valueList.Attributes == null)
            {
                valueList.CreateAttributes();
            }

            var pivot = Attributes.Pivot;
            var offset = new System.Drawing.PointF(pivot.X - 250, pivot.Y - 11);

            // Remove any existing connected value list
            var sources = Sources;
            if (sources.Count > 0)
            {
                foreach (var source in sources)
                {
                    if (source is GH_ValueList)
                    {
                        document.RemoveObject(source, false);
                    }
                }
            }

            valueList.Attributes.Pivot = offset;
            document.AddObject(valueList, false);
            AddSource(valueList);
        }

        public override Guid ComponentGuid => new Guid("D69E7A83-7F67-46E8-8FDD-A0CF812B22D4");

        public static List<string> GetFileNames()
        {
            string currentFilePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string currentDirectory = Path.GetDirectoryName(currentFilePath);
            string folderPath = Path.Combine(currentDirectory, "RhinoFiles", "TypicalLevelShape");

            if (!Directory.Exists(folderPath))
            {
                return new List<string>();
            }

            var files = Directory.GetFiles(folderPath, "*.3dm");
            var fileNames = new List<string>();
            foreach (var file in files)
            {
                fileNames.Add(Path.GetFileNameWithoutExtension(file));
            }

            return fileNames;
        }


    

        public static List<string> GetJustificationTypes()
        {
            return new List<string>
                {
                    "BottomLeft",
                    "BottomCenter",
                    "BottomRight",
                    "MiddleLeft",
                    "MiddleCenter",
                    "MiddleRight",
                    "TopLeft",
                    "TopCenter",
                    "TopRight"
                };
        }
        public static List<string> GetRegulation()
        {
            return new List<string>
                {
                    "1245/BXD-KHCN",
                    "34/2024/QĐ-UBND TP Hà Nội"
                };
        }

        public static List<string> GetTemplateType()
        {
            return new List<string>
            {
                "T",
                "Z",
                "I",
                "L",
            };
        }

        public static List<string> GetGDocObjType()
        {
            return new List<string>
            {
                "GGeometry",
                "GHatchObj",
                "GBlockInstance",
                "GTextObj",
            };
        }

       

        public static List<string> GetImageSizes()
        {
            return new List<string>
            {
                "1K",
                "2K",
                "4K",
            };
        }

        public static List<string> GetMediaResolutions()
        {
            return new List<string>
            {
                "LOW",
                "MEDIUM",
                "HIGH"
            };
        }

        public static List<string> GetAspectRatios()
        {
            return new List<string>
            {
                "1:1",
                "2:3",
                "3:2",
                "3:4",
                "4:3",
                "4:5",
                "5:4",
                "9:16",
                "16:9",
                "21:9"
            };
        }
    }
}