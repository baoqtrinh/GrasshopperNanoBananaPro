using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Glab.C_Documentation;
using Glab.Utilities;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SD = System.Drawing;
using Rhino;

namespace Glab.C_AI.Gemini
{
    /// <summary>
    /// Grasshopper component that generates images using Google's Gemini Imagen API
    /// </summary>
    public class GeminiImageGenerator : GH_Component
    {
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };
        private const string API_KEY = ""; // Replace with your Gemini API key (optional if supplying per-component)
        private const string MODEL_ID = "gemini-3-pro-image-preview"; // Native image generation model

        private GImage _generatedImage;
        private string _lastError;
        private string _lastRawResponse;
        private string _lastTextResponse;
        private bool _isGenerating = false;

        // Store pending request parameters
        private string _pendingPrompt;
        private List<GImage> _pendingInputImages;
        private string _pendingImageSize;
        private string _pendingAspectRatio;
        private GenerationResult _pendingResult;

        /// <summary>
        /// Initializes a new instance of the GeminiImageGenerator class.
        /// </summary>
        public GeminiImageGenerator()
          : base("Gemini Image Generator", "GeminiImg",
              "Generates images using Google Gemini 3 with native image generation",
              "Glab", "AI")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Prompt", "P", "Text prompt describing the image to generate or edit", GH_ParamAccess.item);
            pManager.AddGenericParameter("Input Images", "Img", "Optional input images (GImage list, up to 14) for image editing. Leave empty for text-to-image generation.", GH_ParamAccess.list);

            // Use custom parameters with value list support
            var imageSizeParam = new CustomTextParameter("ImageSize")
            {
                Name = "Image Size",
                NickName = "S",
                Description = "Output image size: 1K, 2K, or 4K",
                Access = GH_ParamAccess.item
            };
            imageSizeParam.SetPersistentData("1K");
            pManager.AddParameter(imageSizeParam);

            var aspectRatioParam = new CustomTextParameter("AspectRatio")
            {
                Name = "Aspect Ratio",
                NickName = "A",
                Description = "Output aspect ratio: 1:1, 2:3, 3:2, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9, 21:9",
                Access = GH_ParamAccess.item
            };
            aspectRatioParam.SetPersistentData("1:1");
            pManager.AddParameter(aspectRatioParam);

            pManager.AddBooleanParameter("Generate", "G", "Click button to generate image", GH_ParamAccess.item, false);

            // New optional text input for API Key (overrides embedded API_KEY if provided)
            pManager.AddTextParameter("API Key", "K", "API key", GH_ParamAccess.item);
            // Make optional inputs
            pManager[1].Optional = true; // Input images - optional for text-to-image
            pManager[3].Optional = true; // Aspect ratio - optional
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("GImage", "I", "Generated image as GImage object (connect to PreviewImage component)", GH_ParamAccess.item);
            pManager.AddTextParameter("Text", "T", "Text response from the model (if any)", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "S", "Generation status message", GH_ParamAccess.item);
            pManager.AddTextParameter("Raw Response", "R", "Raw API response JSON (base64 image data truncated)", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string prompt = null;
            var inputImageGoos = new List<IGH_Goo>();
            string imageSize = "1K";
            string aspectRatio = "1:1";
            bool generate = false;
            string userApiKey = null;

            // Get inputs
            if (!DA.GetData(0, ref prompt)) return;
            DA.GetDataList(1, inputImageGoos); // Optional input images (list)
            DA.GetData(2, ref imageSize);
            DA.GetData(3, ref aspectRatio); // Optional aspect ratio
            if (!DA.GetData(4, ref generate)) return;
            // Optional API key input (index 5)
            DA.GetData(5, ref userApiKey);

            // Try to extract GImages from input list - handle multiple wrapper types
            var inputImages = new List<GImage>();
            foreach (var inputImageGoo in inputImageGoos)
            {
                if (inputImageGoo == null) continue;

                GImage extractedImage = null;

                // Direct GImage (shouldn't happen but just in case)
                if (inputImageGoo is GImage directGImage)
                {
                    extractedImage = directGImage;
                }
                // Wrapped in GH_ObjectWrapper
                else if (inputImageGoo is GH_ObjectWrapper wrapper)
                {
                    if (wrapper.Value is GImage gImg)
                    {
                        extractedImage = gImg;
                    }
                }
                // Try to cast the scriptVariable
                else
                {
                    // Try getting the underlying value
                    object underlyingValue = null;
                    if (inputImageGoo is GH_Goo<object> genericGoo)
                    {
                        underlyingValue = genericGoo.Value;
                    }
                    else
                    {
                        // Use reflection as last resort
                        var valueProperty = inputImageGoo.GetType().GetProperty("Value");
                        if (valueProperty != null)
                        {
                            underlyingValue = valueProperty.GetValue(inputImageGoo);
                        }
                    }

                    if (underlyingValue is GImage extractedGImage)
                    {
                        extractedImage = extractedGImage;
                    }
                }

                if (extractedImage != null && extractedImage.Image != null)
                {
                    inputImages.Add(extractedImage);
                }
            }

            // Validate image count (API accepts up to 14 images)
            if (inputImages.Count > 14)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Too many input images ({inputImages.Count}). Using first 14 only. API accepts up to 14 images.");
                inputImages = inputImages.Take(14).ToList();
            }

            // Use embedded API key by default, override with user's input if provided
            string apiKey = API_KEY;
            if (!string.IsNullOrWhiteSpace(userApiKey))
            {
                apiKey = userApiKey.Trim();
            }

            // Validate API key
            if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "API Key not configured. Provide an API key using the 'API Key' input or set the API_KEY constant in GeminiImageGenerator.cs");
                DA.SetData(2, "Error: API Key not configured");
                DA.SetData(3, _lastRawResponse);
                return;
            }

            // Validate prompt
            if (string.IsNullOrEmpty(prompt))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Please provide a prompt.");
                DA.SetData(2, "Waiting for prompt...");
                return;
            }

            // Validate image size
            var validSizes = new HashSet<string> { "1K", "2K", "4K" };
            if (!validSizes.Contains(imageSize.ToUpper()))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Invalid image size '{imageSize}'. Using 1K. Valid options: 1K, 2K, 4K");
                imageSize = "1K";
            }

            // Validate aspect ratio
            var validRatios = new HashSet<string> { "1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9" };
            if (!validRatios.Contains(aspectRatio))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Invalid aspect ratio '{aspectRatio}'. Using 1:1. Valid options: 1:1, 2:3, 3:2, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9, 21:9");
                aspectRatio = "1:1";
            }

            // Check if we have a pending result from background task (highest priority)
            if (_pendingResult != null)
            {
                var result = _pendingResult;
                _pendingResult = null;
                _isGenerating = false;

                if (result.Success)
                {
                    _generatedImage = result.Image;
                    _lastError = null;
                    _lastRawResponse = result.RawResponse;
                    _lastTextResponse = result.TextResponse;
                    DA.SetData(0, new GH_ObjectWrapper(_generatedImage));
                    DA.SetData(1, _lastTextResponse);
                    DA.SetData(2, $"Success! Generated {_generatedImage.Image.Width}x{_generatedImage.Image.Height} image");
                    DA.SetData(3, _lastRawResponse);
                    Message = $"{_generatedImage.Image.Width}x{_generatedImage.Image.Height}";
                }
                else
                {
                    _lastError = result.ErrorMessage;
                    _lastRawResponse = result.RawResponse;
                    _lastTextResponse = result.TextResponse;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, result.ErrorMessage);
                    DA.SetData(1, _lastTextResponse);
                    DA.SetData(2, $"Error: {result.ErrorMessage}");
                    DA.SetData(3, _lastRawResponse);
                    Message = "Error";

                    if (_generatedImage != null)
                    {
                        DA.SetData(0, new GH_ObjectWrapper(_generatedImage));
                    }
                }
                return;
            }

            // If currently generating, show progress
            if (_isGenerating)
            {
                DA.SetData(2, "Generation in progress... please wait");
                if (_generatedImage != null)
                {
                    DA.SetData(0, new GH_ObjectWrapper(_generatedImage));
                    DA.SetData(1, _lastTextResponse);
                }
                DA.SetData(3, _lastRawResponse);
                return;
            }

            // If button is pressed and not currently generating, start generation
            if (generate)
            {
                // Start background generation
                _isGenerating = true;
                _pendingPrompt = prompt;
                _pendingInputImages = inputImages;
                _pendingImageSize = imageSize.ToUpper();
                _pendingAspectRatio = aspectRatio;

                // Update message based on mode
                if (inputImages.Count > 0)
                {
                    Message = $"Editing ({inputImages.Count} images)...";
                }
                else
                {
                    Message = "Generating...";
                }

                // Output current state while generating
                DA.SetData(1, _lastTextResponse);
                DA.SetData(2, inputImages.Count > 0 ? $"Generation started with {inputImages.Count} input image(s)... please wait" : "Generation started... please wait");
                if (_generatedImage != null)
                {
                    DA.SetData(0, new GH_ObjectWrapper(_generatedImage));
                }
                DA.SetData(3, _lastRawResponse);

                // Capture apiKey for the closure
                string capturedApiKey = apiKey;

                // Start async task in background
                Task.Run(async () =>
                {
                    GenerationResult result = null;
                    try
                    {
                        result = await GenerateImageAsync(capturedApiKey, _pendingPrompt, _pendingInputImages, _pendingImageSize, _pendingAspectRatio);
                    }
                    catch (Exception ex)
                    {
                        result = new GenerationResult { Success = false, ErrorMessage = $"Task error: {ex.Message}", RawResponse = ex.ToString() };
                    }

                    // Store result
                    _pendingResult = result;
                    _isGenerating = false;

                    // Schedule solution update on UI thread
                    try
                    {
                        RhinoApp.InvokeOnUiThread((Action)(() =>
                        {
                            try
                            {
                                ExpireSolution(true);
                            }
                            catch { }
                        }));
                    }
                    catch { }
                });

                return;
            }

            // Not generating, output last results if available
            if (_generatedImage != null)
            {
                DA.SetData(0, new GH_ObjectWrapper(_generatedImage));
                DA.SetData(1, _lastTextResponse);
                DA.SetData(2, "Ready (last image available). Click button to generate new.");
            }
            else
            {
                DA.SetData(2, "Ready to generate. Click button to generate.");
            }
            DA.SetData(3, _lastRawResponse);
        }

        /// <summary>
        /// Generates an image using the Gemini API with native image generation
        /// </summary>
        private async Task<GenerationResult> GenerateImageAsync(string apiKey, string prompt, List<GImage> inputImages, string imageSize, string aspectRatio)
        {
            try
            {
                // Use generateContent endpoint for Gemini image generation
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{MODEL_ID}:generateContent";

                // Build the parts array - text first, then images (matching Google's example)
                var parts = new List<object>();

                // Add text prompt first
                parts.Add(new { text = prompt });

                // Add input images if provided (up to 14 images supported)
                if (inputImages != null && inputImages.Count > 0)
                {
                    foreach (var inputImage in inputImages)
                    {
                        if (inputImage?.Image == null) continue;

                        string base64Image = ConvertGImageToBase64(inputImage);
                        parts.Add(new
                        {
                            inline_data = new
                            {
                                mime_type = "image/png",
                                data = base64Image
                            }
                        });
                    }
                }

                // Build request body with image size and aspect ratio config
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = parts
                        }
                    },
                    generationConfig = new
                    {
                        responseModalities = new[] { "TEXT", "IMAGE" },
                        imageConfig = new
                        {
                            imageSize = imageSize,  // "1K", "2K", or "4K"
                            aspectRatio = aspectRatio  // "1:1", "16:9", etc.
                        }
                    }
                };

                string jsonBody = JsonConvert.SerializeObject(requestBody);

                // Create request with proper headers
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // Send the request
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Try to parse error message
                    try
                    {
                        var errorJson = JObject.Parse(responseContent);
                        var errorMessage = errorJson["error"]?["message"]?.ToString() ?? responseContent;
                        return new GenerationResult { Success = false, ErrorMessage = $"API Error ({response.StatusCode}): {errorMessage}", RawResponse = responseContent };
                    }
                    catch
                    {
                        return new GenerationResult { Success = false, ErrorMessage = $"API Error ({response.StatusCode}): {responseContent}", RawResponse = responseContent };
                    }
                }

                // Parse the response - Gemini 2.0 format
                var responseJson = JObject.Parse(responseContent);

                // Create a truncated version of raw response for output
                string truncatedResponse = TruncateBase64InResponse(responseContent);

                // Extract candidates
                var candidates = responseJson["candidates"] as JArray;
                if (candidates == null || candidates.Count == 0)
                {
                    return new GenerationResult { Success = false, ErrorMessage = "No candidates in response", RawResponse = truncatedResponse };
                }

                var firstCandidate = candidates[0];
                var contentParts = firstCandidate["content"]?["parts"] as JArray;

                if (contentParts == null || contentParts.Count == 0)
                {
                    return new GenerationResult { Success = false, ErrorMessage = "No content parts in response", RawResponse = truncatedResponse };
                }

                // Look for image and text parts
                GImage resultImage = null;
                string textResponse = null;

                foreach (var part in contentParts)
                {
                    // Check for inline image data (API returns camelCase: inlineData)
                    var inlineData = part["inlineData"] ?? part["inline_data"];
                    if (inlineData != null)
                    {
                        var mimeType = (inlineData["mimeType"] ?? inlineData["mime_type"])?.ToString();
                        var base64Data = inlineData["data"]?.ToString();

                        if (!string.IsNullOrEmpty(base64Data) && mimeType?.StartsWith("image/") == true)
                        {
                            byte[] imageBytes = Convert.FromBase64String(base64Data);
                            using (var memoryStream = new MemoryStream(imageBytes))
                            {
                                var isImage = Image.Load<Rgba32>(memoryStream);
                                resultImage = new GImage(new Rectangle(0, 0, isImage.Width, isImage.Height), SD.Color.White)
                                {
                                    Image = isImage
                                };
                            }
                        }
                    }

                    // Check for text
                    var text = part["text"]?.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        textResponse = text;
                    }
                }

                if (resultImage == null)
                {
                    return new GenerationResult
                    {
                        Success = false,
                        ErrorMessage = "No image generated in response. The model may have returned text only.",
                        RawResponse = truncatedResponse,
                        TextResponse = textResponse
                    };
                }

                return new GenerationResult
                {
                    Success = true,
                    Image = resultImage,
                    RawResponse = truncatedResponse,
                    TextResponse = textResponse
                };
            }
            catch (HttpRequestException ex)
            {
                return new GenerationResult { Success = false, ErrorMessage = $"Network error: {ex.Message}" };
            }
            catch (JsonException ex)
            {
                return new GenerationResult { Success = false, ErrorMessage = $"JSON parsing error: {ex.Message}" };
            }
            catch (Exception ex)
            {
                return new GenerationResult { Success = false, ErrorMessage = $"Unexpected error: {ex.Message}" };
            }
        }

        /// <summary>
        /// Converts a GImage to a base64 encoded PNG string
        /// </summary>
        private string ConvertGImageToBase64(GImage gImage)
        {
            if (gImage?.Image == null)
                return null;

            using (var memoryStream = new MemoryStream())
            {
                gImage.Image.Save(memoryStream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                return Convert.ToBase64String(memoryStream.ToArray());
            }
        }

        /// <summary>
        /// Truncates base64 image data in JSON response to make it readable
        /// </summary>
        private string TruncateBase64InResponse(string jsonResponse)
        {
            try
            {
                var json = JObject.Parse(jsonResponse);

                // Handle Gemini 2.0 format (candidates -> content -> parts -> inlineData)
                var candidates = json["candidates"] as JArray;
                if (candidates != null)
                {
                    foreach (var candidate in candidates)
                    {
                        var parts = candidate["content"]?["parts"] as JArray;
                        if (parts != null)
                        {
                            foreach (var part in parts)
                            {
                                var inlineData = part["inlineData"];
                                if (inlineData != null)
                                {
                                    var data = inlineData["data"]?.ToString();
                                    if (!string.IsNullOrEmpty(data) && data.Length > 100)
                                    {
                                        inlineData["data"] = $"[BASE64_DATA: {data.Length} chars - truncated]";
                                    }
                                }
                            }
                        }
                    }
                }

                // Also handle old Imagen format (predictions -> bytesBase64Encoded)
                var predictions = json["predictions"] as JArray;
                if (predictions != null)
                {
                    foreach (var prediction in predictions)
                    {
                        var base64 = prediction["bytesBase64Encoded"]?.ToString();
                        if (!string.IsNullOrEmpty(base64) && base64.Length > 100)
                        {
                            prediction["bytesBase64Encoded"] = $"[BASE64_DATA: {base64.Length} chars - truncated]";
                        }
                    }
                }

                return json.ToString(Formatting.Indented);
            }
            catch
            {
                return jsonResponse;
            }
        }

        /// <summary>
        /// Result class for image generation
        /// </summary>
        private class GenerationResult
        {
            public bool Success { get; set; }
            public GImage Image { get; set; }
            public string ErrorMessage { get; set; }
            public string RawResponse { get; set; }
            public string TextResponse { get; set; }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("A7B3C8D2-E4F5-6789-0ABC-DEF123456789");

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override SD.Bitmap Icon => null;
    }
}