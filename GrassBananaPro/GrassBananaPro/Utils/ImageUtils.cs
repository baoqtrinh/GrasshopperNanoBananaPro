using Glab.C_Documentation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using SD = System.Drawing;

namespace Glab.Utilities
{
    public partial class ImageUtils
    {
        /// <summary>
        /// Saves an image to the specified file path
        /// </summary>
        /// <param name="image">The image to save</param>
        /// <param name="filePath">Path where the image will be saved</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        public static bool SaveImage(Image<Rgba32> image, string filePath)
        {
            try
            {
                // Ensure directory exists
                string directory = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Save the image
                image.Save(filePath);
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception details for better debugging
                Console.WriteLine($"Error saving image to {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Overlaps multiple images on top of each other in sequence
        /// </summary>
        /// <param name="images">List of images to overlap, with the first being the base</param>
        /// <param name="positions">List of positions (X,Y) for each overlay image, must match the length of images minus 1</param>
        /// <param name="opacities">List of opacity values for each overlay image, must match the length of images minus 1</param>
        /// <returns>A new Image containing all overlapped images</returns>
        public static Image<Rgba32> OverlapImages(
            List<Image<Rgba32>> images,
            List<(int X, int Y)> positions = null,
            List<float> opacities = null)
        {
            if (images == null || images.Count == 0)
                return null;

            // Use the first image as the base
            var baseImage = images[0].Clone();

            // If there's only one image, return it
            if (images.Count == 1)
                return baseImage;

            // Ensure positions and opacities lists are properly initialized
            if (positions == null || positions.Count == 0)
            {
                positions = new List<(int, int)>();
                for (int i = 0; i < images.Count - 1; i++)
                    positions.Add((0, 0));
            }

            if (opacities == null || opacities.Count == 0)
            {
                opacities = new List<float>();
                for (int i = 0; i < images.Count - 1; i++)
                    opacities.Add(1.0f);
            }

            // Ensure positions and opacities lists match the number of overlay images
            while (positions.Count < images.Count - 1)
                positions.Add((0, 0));

            while (opacities.Count < images.Count - 1)
                opacities.Add(1.0f);

            // Overlap each image onto the base image in sequence
            for (int i = 1; i < images.Count; i++)
            {
                if (images[i] == null)
                    continue;

                // Get position and opacity for this overlay
                var position = positions[i - 1];
                float opacity = Math.Min(1.0f, Math.Max(0.0f, opacities[i - 1])); // Ensure opacity is between 0 and 1

                // Mutate the base image to overlay the current image
                baseImage.Mutate(ctx =>
                {
                    var options = new GraphicsOptions
                    {
                        AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver,
                        Antialias = true,
                        BlendPercentage = opacity
                    };

                    ctx.DrawImage(images[i], new SixLabors.ImageSharp.Point(position.X, position.Y), options);
                });
            }

            return baseImage;
        }

        /// <summary>
        /// Overlaps multiple GImages on top of each other in sequence and returns a new GImage
        /// </summary>
        /// <param name="images">List of GImages to overlap, with the first being the base</param>
        /// <param name="positions">List of positions (X,Y) for each overlay image, must match the length of images minus 1</param>
        /// <param name="opacities">List of opacity values for each overlay image, must match the length of images minus 1</param>
        /// <returns>A new GImage containing all overlapped images</returns>
        public static GImage OverlapGImages(
            List<GImage> images,
            List<(int X, int Y)> positions = null,
            List<float> opacities = null)
        {
            if (images == null || images.Count == 0 || images[0] == null || images[0].Image == null)
                return null;

            // Extract ImageSharp images from GImages
            var imageSharpImages = new List<Image<Rgba32>>();
            foreach (var gImage in images)
            {
                if (gImage != null && gImage.Image != null)
                    imageSharpImages.Add(gImage.Image);
            }

            // Perform the overlap operation
            var resultImage = OverlapImages(imageSharpImages, positions, opacities);
            if (resultImage == null)
                return null;

            // Create a new GImage with the result
            var resultGImage = new GImage(new Rectangle(0, 0, resultImage.Width, resultImage.Height), SD.Color.Transparent);
            resultGImage.Image = resultImage;

            return resultGImage;
        }

        /// <summary>
        /// Overlaps a base image with an overlay image, with optional scaling for the overlay
        /// </summary>
        /// <param name="baseImage">The base image</param>
        /// <param name="overlayImage">The image to overlay on the base image</param>
        /// <param name="position">Position (X,Y) for the overlay image</param>
        /// <param name="opacity">Opacity value for the overlay image (0.0-1.0)</param>
        /// <param name="scale">Scale factor for the overlay image (1.0 = original size)</param>
        /// <returns>A new Image containing the base image with overlay applied</returns>
        public static Image<Rgba32> OverlapImage(
            Image<Rgba32> baseImage,
            Image<Rgba32> overlayImage,
            (int X, int Y) position = default,
            float opacity = 1.0f,
            float scale = 1.0f)
        {
            if (baseImage == null)
                return null;

            if (overlayImage == null)
                return baseImage.Clone();

            // Clone the base image to work with
            var resultImage = baseImage.Clone();

            // Ensure opacity is between 0 and 1
            opacity = Math.Min(1.0f, Math.Max(0.0f, opacity));

            // Apply the overlay
            resultImage.Mutate(ctx =>
            {
                // Resize the overlay image if scale is not 1.0
                var imageToOverlay = overlayImage;
                if (scale != 1.0f && scale > 0)
                {
                    // Calculate the new dimensions
                    int newWidth = (int)Math.Round(overlayImage.Width * scale);
                    int newHeight = (int)Math.Round(overlayImage.Height * scale);

                    // Create a resized copy of the overlay image
                    var resizedOverlay = overlayImage.Clone();
                    resizedOverlay.Mutate(x => x.Resize(newWidth, newHeight));
                    imageToOverlay = resizedOverlay;
                }

                var options = new GraphicsOptions
                {
                    AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver,
                    Antialias = true,
                    BlendPercentage = opacity
                };

                ctx.DrawImage(imageToOverlay, new SixLabors.ImageSharp.Point(position.X, position.Y), options);
            });

            return resultImage;
        }

        /// <summary>
        /// Overlaps a GImage base with a GImage overlay
        /// </summary>
        /// <param name="baseImage">The base GImage</param>
        /// <param name="overlayImage">The GImage to overlay</param>
        /// <param name="position">Position (X,Y) for the overlay image</param>
        /// <param name="opacity">Opacity value for the overlay image (0.0-1.0)</param>
        /// <param name="scale">Scale factor for the overlay image (1.0 = original size)</param>
        /// <returns>A new GImage containing the base with overlay applied</returns>
        public static GImage OverlapGImage(
            GImage baseImage,
            GImage overlayImage,
            (int X, int Y) position = default,
            float opacity = 1.0f,
            float scale = 1.0f)
        {
            if (baseImage == null || baseImage.Image == null)
                return null;

            if (overlayImage == null || overlayImage.Image == null)
                return baseImage;

            // Perform the overlap operation
            var resultImage = OverlapImage(baseImage.Image, overlayImage.Image, position, opacity, scale);
            if (resultImage == null)
                return null;

            // Create a new GImage with the result
            var resultGImage = new GImage(new Rectangle(0, 0, resultImage.Width, resultImage.Height), SD.Color.Transparent);
            resultGImage.Image = resultImage;

            return resultGImage;
        }

        /// <summary>
        /// Rotates a GImage by a specified number of 90-degree clockwise rotations
        /// </summary>
        /// <param name="gImage">The GImage to rotate</param>
        /// <param name="rotations">Number of 90-degree clockwise rotations (0-3, values outside this range will be normalized)</param>
        /// <returns>A new GImage with the rotation applied, or null if input is invalid</returns>
        public static GImage RotateGImage(GImage gImage, int rotations)
        {
            if (gImage == null || gImage.Image == null)
                return null;

            // Normalize rotations to 0-3 range
            rotations = ((rotations % 4) + 4) % 4;

            // If no rotation needed, return a copy
            if (rotations == 0)
            {
                var copyImage = gImage.Image.Clone();
                var resultGImage = new GImage(new Rectangle(0, 0, copyImage.Width, copyImage.Height), SD.Color.Transparent);
                resultGImage.Image = copyImage;
                return resultGImage;
            }

            // Apply the rotation
            var rotatedImage = gImage.Image.Clone();

            rotatedImage.Mutate(ctx =>
            {
                switch (rotations)
                {
                    case 1: // 90 degrees clockwise
                        ctx.Rotate(RotateMode.Rotate90);
                        break;
                    case 2: // 180 degrees
                        ctx.Rotate(RotateMode.Rotate180);
                        break;
                    case 3: // 270 degrees clockwise (or 90 degrees counter-clockwise)
                        ctx.Rotate(RotateMode.Rotate270);
                        break;
                }
            });

            // Create a new GImage with the rotated dimensions
            var rotatedGImage = new GImage(new Rectangle(0, 0, rotatedImage.Width, rotatedImage.Height), SD.Color.Transparent);
            rotatedGImage.Image = rotatedImage;

            return rotatedGImage;

        }

        /// <summary>
        /// Flips a GImage horizontally or vertically
        /// </summary>
        /// <param name="gImage">The GImage to flip</param>
        /// <param name="flipHorizontally">If true, flips horizontally (left-right); if false, flips vertically (up-down)</param>
        /// <returns>A new GImage with the flip applied, or null if input is invalid</returns>
        public static GImage FlipGImage(GImage gImage, bool flipHorizontally)
        {
            if (gImage == null || gImage.Image == null)
                return null;

            // Apply the flip
            var flippedImage = gImage.Image.Clone();

            flippedImage.Mutate(ctx =>
            {
                if (flipHorizontally)
                {
                    ctx.Flip(FlipMode.Horizontal);
                }
                else
                {
                    ctx.Flip(FlipMode.Vertical);
                }
            });

            // Create a new GImage with the flipped image (dimensions remain the same)
            var flippedGImage = new GImage(new Rectangle(0, 0, flippedImage.Width, flippedImage.Height), SD.Color.Transparent);
            flippedGImage.Image = flippedImage;

            return flippedGImage;
        }

        /// <summary>
        /// Flips a GImage horizontally (left-right mirror)
        /// </summary>
        /// <param name="gImage">The GImage to flip horizontally</param>
        /// <returns>A new GImage with horizontal flip applied, or null if input is invalid</returns>
        public static GImage FlipGImageHorizontally(GImage gImage)
        {
            return FlipGImage(gImage, true);
        }

        /// <summary>
        /// Flips a GImage vertically (up-down mirror)
        /// </summary>
        /// <param name="gImage">The GImage to flip vertically</param>
        /// <returns>A new GImage with vertical flip applied, or null if input is invalid</returns>
        public static GImage FlipGImageVertically(GImage gImage)
        {
            return FlipGImage(gImage, false);
        }


    }
}
