using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Glab.Utilities;
using SD = System.Drawing;
using WpfPoint = System.Windows.Point;

namespace Glab.C_Documentation.DrawImg
{
    /// <summary>
    /// WPF Window for painting masks on images
    /// </summary>
    public partial class MaskPainterWindow : Window
    {
        private readonly ImageMaskPainter _component;
        private Image<Rgba32> _originalImage; // Full resolution original
        private Image<Rgba32> _workingImage; // Scaled down for editing (max 1000px)
        private Image<Rgba32> _maskLayer; // Mask at working resolution
        private WriteableBitmap _displayBitmap;
        private double _workingScale = 1.0; // Scale factor from original to working size

        // Preview (downscaled) buffers
        private const double PreviewScale = 0.50; // 50% resolution while drawing for better performance
        private Image<Rgba32> _originalImagePreview;
        private Image<Rgba32> _maskLayerPreview;
        private WriteableBitmap _previewBitmap; // Composited preview
        private WriteableBitmap _maskOnlyBitmap; // ONLY the mask layer for preview

        private bool _isDrawing = false;
        private WpfPoint _lastPoint;
        private readonly List<WpfPoint> _strokePoints = new List<WpfPoint>();
        private int _previewUpdateCounter = 0; // Counter for batching preview updates
        private System.Windows.Controls.Image _previewMaskImage; // Separate WPF Image for mask overlay

        private SD.Color _maskColor = SD.Color.FromArgb(255, 255, 0, 0); // Always fully opaque
        private int _brushSize = 20;
        private double _opacity = 1.0; // fixed opacity (not used for mask)
        private bool _isPaintMode = true;

        private Stack<Image<Rgba32>> _undoStack = new Stack<Image<Rgba32>>();
        private const int MAX_UNDO_STEPS = 20;

        // Accumulated dirty rect during stroke (full-res)
        private Int32Rect? _accDirty;

        private Ellipse _cursorEllipse;

        // Zoom/Pan state
        private double _zoom = 1.0;
        private const double ZoomMin = 0.1;
        private const double ZoomMax = 10.0;
        private const double ZoomWheelStep = 1.1; // 10% per wheel notch
        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        private TransformGroup _transformGroup;
        private bool _isPanning = false;
        private WpfPoint _lastPanPoint;

        public MaskPainterWindow(ImageMaskPainter component)
        {
            InitializeComponent();
            _component = component;
            
            // Initialize from component
            var inputImage = _component.GetInputImage();
            if (inputImage?.Image != null)
            {
                _originalImage = inputImage.Image.Clone();
                
                // Scale down to max 1000px on larger dimension for performance
                int maxDim = Math.Max(_originalImage.Width, _originalImage.Height);
                if (maxDim > 1000)
                {
                    _workingScale = 1000.0 / maxDim;
                    int newWidth = (int)Math.Round(_originalImage.Width * _workingScale);
                    int newHeight = (int)Math.Round(_originalImage.Height * _workingScale);
                    _workingImage = _originalImage.Clone(ctx => ctx.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
                }
                else
                {
                    _workingScale = 1.0;
                    _workingImage = _originalImage.Clone();
                }
                
                var maskLayer = _component.GetMaskLayer();
                if (maskLayer != null && 
                    maskLayer.Width == _originalImage.Width && 
                    maskLayer.Height == _originalImage.Height)
                {
                    // Scale down existing mask to working resolution
                    if (_workingScale < 1.0)
                    {
                        _maskLayer = maskLayer.Clone(ctx => ctx.Resize(_workingImage.Width, _workingImage.Height, KnownResamplers.NearestNeighbor));
                    }
                    else
                    {
                        _maskLayer = maskLayer.Clone();
                    }
                }
                else
                {
                    _maskLayer = new Image<Rgba32>(_workingImage.Width, _workingImage.Height);
                    ClearMask();
                }
            }

            // Override defaults from component inputs
            if (_component != null)
            {
                _maskColor = _component.GetInitialMaskColor();
                _brushSize = _component.GetInitialBrushSize();
            }

            SetupUI();
            InitializeDisplay();
            InitializeTransforms();
            this.Loaded += (s, e) =>
            {
                // Delay to ensure layout/ScrollViewer has measured
                Dispatcher.BeginInvoke(() => FitImageToWindow());
                var sv = FindAncestor<ScrollViewer>(ImageCanvas);
                if (sv != null)
                {
                    sv.SizeChanged += (s2, e2) => FitImageToWindow();
                }
            };
            HookPointerHoverHandlers();
            EnsureCursorVisual();
            UpdateCursorVisual();
            HookPanHandlers();
            WireZoomSliderIfExists();
            UpdateDisplay();
        }

        private void InitializeTransforms()
        {
            _scaleTransform = new ScaleTransform(1, 1);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
            ImageCanvas.RenderTransform = _transformGroup;
            ImageCanvas.MouseWheel += ImageCanvas_MouseWheel; // enable wheel zoom
        }

        private void HookPanHandlers()
        {
            ImageCanvas.MouseDown += ImageCanvas_MouseDown_ForPan;
            ImageCanvas.MouseUp += ImageCanvas_MouseUp_ForPan;
            ImageCanvas.MouseMove += ImageCanvas_MouseMove_ForPan;
        }

        private WpfPoint GetViewportCenterAnchor()
        {
            // Center point in the visual viewport (CanvasContainer or window fallback)
            double vw = CanvasContainer?.ActualWidth > 0 ? CanvasContainer.ActualWidth : this.ActualWidth;
            double vh = CanvasContainer?.ActualHeight > 0 ? CanvasContainer.ActualHeight : this.ActualHeight;
            var viewportCenter = new WpfPoint(vw / 2.0, vh / 2.0);
            // Convert to ImageCanvas local coordinates using inverse of current transform
            if (_transformGroup != null && _transformGroup.Inverse != null)
            {
                return _transformGroup.Inverse.Transform(viewportCenter);
            }
            return viewportCenter;
        }

        private void ApplyZoomWithScreenAnchor(double targetZoom, WpfPoint anchorScreen)
        {
            targetZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, targetZoom));
            double oldZoom = _scaleTransform.ScaleX;
            if (Math.Abs(targetZoom - oldZoom) < 0.000001) return;
            double tx = _translateTransform.X;
            double ty = _translateTransform.Y;
            double anchorWorldX = (anchorScreen.X - tx) / oldZoom;
            double anchorWorldY = (anchorScreen.Y - ty) / oldZoom;
            double newTx = anchorScreen.X - targetZoom * anchorWorldX;
            double newTy = anchorScreen.Y - targetZoom * anchorWorldY;
            _scaleTransform.ScaleX = targetZoom;
            _scaleTransform.ScaleY = targetZoom;
            _translateTransform.X = newTx;
            _translateTransform.Y = newTy;
            _zoom = targetZoom;
            if (FindName("SliderZoom") is Slider slider && Math.Abs(slider.Value - _zoom) > 0.0001)
                slider.Value = _zoom;
            UpdateZoomPercent();
        }

        private void UpdateZoomPercent()
        {
            if (FindName("ZoomPercentText") is TextBlock tb)
            {
                tb.Text = $"{Math.Round(_zoom * 100)}%";
            }
        }

        private void SetZoom(double newZoom, WpfPoint anchorWorldAssumed)
        {
            var screenCenter = new WpfPoint(
                CanvasContainer?.ActualWidth > 0 ? CanvasContainer.ActualWidth / 2.0 : ActualWidth / 2.0,
                CanvasContainer?.ActualHeight > 0 ? CanvasContainer.ActualHeight / 2.0 : ActualHeight / 2.0);
            ApplyZoomWithScreenAnchor(newZoom, screenCenter);
        }

        private void ImageCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_workingImage == null) return;
            double direction = e.Delta > 0 ? 1 : -1;
            double stepFrac = 0.08; // fixed 8%
            double target = _scaleTransform.ScaleX * (1 + direction * stepFrac);
            var anchorScreen = e.GetPosition(CanvasContainer as IInputElement ?? this);
            ApplyZoomWithScreenAnchor(target, anchorScreen);
            e.Handled = true;
        }

        private void WireZoomSliderIfExists()
        {
            if (this.FindName("SliderZoom") is Slider zoom)
            {
                zoom.Minimum = ZoomMin;
                zoom.Maximum = ZoomMax;
                zoom.Value = _zoom;
                zoom.ValueChanged += (s, e) =>
                {
                    var anchor = GetViewportCenterAnchor();
                    SetZoom(zoom.Value, anchor);
                };
                UpdateZoomPercent();
            }
        }

        private void ApplyFit(double viewportW, double viewportH)
        {
            double sx = viewportW / _workingImage.Width;
            double sy = viewportH / _workingImage.Height;
            double fitScale = Math.Min(sx, sy);
            _zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, fitScale));
            _scaleTransform.ScaleX = _zoom;
            _scaleTransform.ScaleY = _zoom;
            double cx = (viewportW - _workingImage.Width * _zoom) / 2.0;
            double cy = (viewportH - _workingImage.Height * _zoom) / 2.0;
            _translateTransform.X = cx;
            _translateTransform.Y = cy;
            UpdateZoomPercent();
        }

        private void FitImageToWindow()
        {
            if (_workingImage == null) return;
            var sv = FindAncestor<ScrollViewer>(ImageCanvas);
            if (sv == null)
            {
                double viewportWw = this.ActualWidth;
                double viewportHw = this.ActualHeight;
                if (viewportWw <= 0 || viewportHw <= 0) return;
                ApplyFit(viewportWw, viewportHw);
                return;
            }
            double viewportW = sv.ViewportWidth;
            double viewportH = sv.ViewportHeight;
            if (viewportW <= 0 || viewportH <= 0)
            {
                viewportW = sv.ActualWidth;
                viewportH = sv.ActualHeight;
            }
            if (viewportW <= 0 || viewportH <= 0) return;
            ApplyFit(viewportW, viewportH);
        }

        private void HookPointerHoverHandlers()
        {
            ImageCanvas.MouseEnter += ImageCanvas_MouseEnter;
            ImageCanvas.MouseLeave += ImageCanvas_MouseLeave;
            ImageCanvas.MouseMove += ImageCanvas_MouseMove_ForCursor;
        }

        private void EnsureCursorVisual()
        {
            if (_cursorEllipse != null) return;
            _cursorEllipse = new Ellipse
            {
                IsHitTestVisible = false,
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed
            };
            // Add on top of everything - higher Z-index than mask overlay (100)
            ImageCanvas.Children.Add(_cursorEllipse);
            Canvas.SetZIndex(_cursorEllipse, 200); // Higher than preview mask layer
        }

        private void UpdateCursorVisual()
        {
            if (_cursorEllipse == null) return;
            double size = Math.Max(1, _brushSize);
            _cursorEllipse.Width = size;
            _cursorEllipse.Height = size;
            // Always show black outline regardless of paint/erase or mask color
            _cursorEllipse.Stroke = Brushes.Black;
            _cursorEllipse.Fill = Brushes.Transparent;
        }

        private void UpdateCursorPosition(WpfPoint p)
        {
            if (_cursorEllipse == null) return;
            double left = p.X - _cursorEllipse.Width / 2.0;
            double top = p.Y - _cursorEllipse.Height / 2.0;
            Canvas.SetLeft(_cursorEllipse, left);
            Canvas.SetTop(_cursorEllipse, top);
        }

        private void ImageCanvas_MouseEnter(object sender, MouseEventArgs e)
        {
            ImageCanvas.Cursor = Cursors.None;
            if (_cursorEllipse != null)
                _cursorEllipse.Visibility = Visibility.Visible;
            UpdateCursorPosition(e.GetPosition(ImageCanvas));
        }

        private void ImageCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            ImageCanvas.Cursor = Cursors.Arrow;
            if (_cursorEllipse != null)
                _cursorEllipse.Visibility = Visibility.Collapsed;
        }

        private void ImageCanvas_MouseMove_ForCursor(object sender, MouseEventArgs e)
        {
            if (_cursorEllipse != null && _cursorEllipse.Visibility == Visibility.Visible)
            {
                UpdateCursorPosition(e.GetPosition(ImageCanvas));
            }
        }

        /// <summary>
        /// Sets up UI event handlers and initial values
        /// </summary>
        private void SetupUI()
        {
            // Initialize slider value from component input
            SliderBrushSize.Value = _brushSize;
            TextBrushSize.Text = _brushSize.ToString();
            
            // Brush size slider
            SliderBrushSize.ValueChanged += (s, e) =>
            {
                _brushSize = (int)SliderBrushSize.Value;
                TextBrushSize.Text = _brushSize.ToString();
                UpdateCursorVisual();
            };

            // Tool selection
            RadioPaint.Checked += (s, e) => { _isPaintMode = true; UpdateCursorVisual(); };
            RadioErase.Checked += (s, e) => { _isPaintMode = false; UpdateCursorVisual(); };

            UpdateColorPreview();
        }

        /// <summary>
        /// Updates the color preview box
        /// </summary>
        private void UpdateColorPreview()
        {
            ColorPreview.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(
                    _maskColor.A,
                    _maskColor.R,
                    _maskColor.G,
                    _maskColor.B));
        }

        private void InitializeDisplay()
        {
            if (_workingImage == null) return;
            _displayBitmap = new WriteableBitmap(
                _workingImage.Width,
                _workingImage.Height,
                96, 96,
                PixelFormats.Bgra32,
                null);
            DisplayImage.Source = _displayBitmap;
            ImageCanvas.Width = _workingImage.Width;
            ImageCanvas.Height = _workingImage.Height;
            MaskCanvas.Width = _workingImage.Width;
            MaskCanvas.Height = _workingImage.Height;

            EnsurePreviewBuffers();
        }

        private void EnsurePreviewBuffers()
        {
            if (_workingImage == null) return;
            int pW = Math.Max(1, (int)Math.Round(_workingImage.Width * PreviewScale));
            int pH = Math.Max(1, (int)Math.Round(_workingImage.Height * PreviewScale));

            if (_originalImagePreview == null || _originalImagePreview.Width != pW || _originalImagePreview.Height != pH)
            {
                _originalImagePreview?.Dispose();
                _originalImagePreview = _workingImage.Clone(ctx => ctx.Resize(pW, pH, KnownResamplers.NearestNeighbor));
            }

            if (_maskLayerPreview == null || _maskLayerPreview.Width != pW || _maskLayerPreview.Height != pH)
            {
                _maskLayerPreview?.Dispose();
                _maskLayerPreview = new Image<Rgba32>(pW, pH);
                // clear transparent
                for (int y = 0; y < pH; y++)
                    for (int x = 0; x < pW; x++)
                        _maskLayerPreview[x, y] = new Rgba32(0, 0, 0, 0);
            }

            if (_previewBitmap == null || _previewBitmap.PixelWidth != pW || _previewBitmap.PixelHeight != pH)
            {
                _previewBitmap = new WriteableBitmap(pW, pH, 96, 96, PixelFormats.Bgra32, null);
            }

            // Create mask-only bitmap for overlay
            if (_maskOnlyBitmap == null || _maskOnlyBitmap.PixelWidth != pW || _maskOnlyBitmap.PixelHeight != pH)
            {
                _maskOnlyBitmap = new WriteableBitmap(pW, pH, 96, 96, PixelFormats.Bgra32, null);
            }
        }

        /// <summary>
        /// Updates the display with current image and mask (entire image)
        /// </summary>
        private void UpdateDisplay()
        {
            if (_workingImage == null || _maskLayer == null || _displayBitmap == null) return;
            UpdateDisplay(new Int32Rect(0, 0, _workingImage.Width, _workingImage.Height));
        }

        /// <summary>
        /// Updates only a region of the display to improve performance.
        /// </summary>
        private void UpdateDisplay(Int32Rect dirtyRect)
        {
            int x0 = Math.Max(0, dirtyRect.X);
            int y0 = Math.Max(0, dirtyRect.Y);
            int x1 = Math.Min(_workingImage.Width, dirtyRect.X + dirtyRect.Width);
            int y1 = Math.Min(_workingImage.Height, dirtyRect.Y + dirtyRect.Height);
            if (x0 >= x1 || y0 >= y1) return;

            _displayBitmap.Lock();
            try
            {
                int stride = _displayBitmap.BackBufferStride;
                // Compose working image + mask for the dirty region
                for (int y = y0; y < y1; y++)
                {
                    IntPtr rowPtr = _displayBitmap.BackBuffer + y * stride + x0 * 4;
                    for (int x = x0; x < x1; x++)
                    {
                        var maskPixel = _maskLayer[x, y];
                        var orig = _workingImage[x, y];
                        byte r, g, b;
                        
                        if (maskPixel.A == 255)
                        {
                            // Fully opaque mask - use mask color directly (solid)
                            r = maskPixel.R;
                            g = maskPixel.G;
                            b = maskPixel.B;
                        }
                        else if (maskPixel.A > 0)
                        {
                            // Partially transparent mask - blend
                            float alpha = maskPixel.A / 255f;
                            r = (byte)(maskPixel.R * alpha + orig.R * (1 - alpha));
                            g = (byte)(maskPixel.G * alpha + orig.G * (1 - alpha));
                            b = (byte)(maskPixel.B * alpha + orig.B * (1 - alpha));
                        }
                        else
                        {
                            // No mask - use original image
                            r = orig.R;
                            g = orig.G;
                            b = orig.B;
                        }
                        
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 0, b);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 1, g);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 2, r);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 3, 255);
                        rowPtr += 4;
                    }
                }
                _displayBitmap.AddDirtyRect(new Int32Rect(x0, y0, x1 - x0, y1 - y0));
            }
            finally
            {
                _displayBitmap.Unlock();
            }
        }

        /// <summary>
        /// Mouse down event - start drawing
        /// </summary>
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDrawing = true;
            _lastPoint = e.GetPosition(ImageCanvas);
            _strokePoints.Clear();
            _strokePoints.Add(_lastPoint);
            _accDirty = new Int32Rect((int)_lastPoint.X, (int)_lastPoint.Y, 1, 1);
            _previewUpdateCounter = 0;

            EnsurePreviewBuffers();
            SyncFullMaskToPreview(); // This already syncs existing mask to preview

            if (_previewBitmap != null)
            {
                DisplayImage.Source = _previewBitmap;
                DisplayImage.RenderTransform = new ScaleTransform(1 / PreviewScale, 1 / PreviewScale);
                RenderOptions.SetBitmapScalingMode(DisplayImage, BitmapScalingMode.NearestNeighbor);
                RenderPreviewBaseImage();

                if (_previewMaskImage == null)
                {
                    _previewMaskImage = new System.Windows.Controls.Image
                    {
                        Stretch = System.Windows.Media.Stretch.None,
                        RenderTransform = new ScaleTransform(1 / PreviewScale, 1 / PreviewScale),
                        IsHitTestVisible = false
                    };
                    RenderOptions.SetBitmapScalingMode(_previewMaskImage, BitmapScalingMode.NearestNeighbor);
                    ImageCanvas.Children.Add(_previewMaskImage);
                    Canvas.SetLeft(_previewMaskImage, 0);
                    Canvas.SetTop(_previewMaskImage, 0);
                    Canvas.SetZIndex(_previewMaskImage, 100);
                }
                
                // SyncFullMaskToPreview already updated _maskOnlyBitmap with existing mask
                _previewMaskImage.Source = _maskOnlyBitmap;
                _previewMaskImage.Visibility = Visibility.Visible;
            }

            PushUndo();
            DrawAtPointPreview(_lastPoint, true);
            DrawAtPoint(_lastPoint, false); // Also draw at working-res immediately
            ImageCanvas.CaptureMouse();
        }

        // Writes ONLY the original preview image (no mask compositing)
        private void RenderPreviewBaseImage()
        {
            if (_originalImagePreview == null || _previewBitmap == null) return;
            _previewBitmap.Lock();
            try
            {
                int pW = _originalImagePreview.Width;
                int pH = _originalImagePreview.Height;
                int stride = _previewBitmap.BackBufferStride;
                for (int y = 0; y < pH; y++)
                {
                    IntPtr rowPtr = _previewBitmap.BackBuffer + y * stride;
                    for (int x = 0; x < pW; x++)
                    {
                        var orig = _originalImagePreview[x, y];
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 0, orig.B);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 1, orig.G);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 2, orig.R);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 3, 255);
                        rowPtr += 4;
                    }
                }
                _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, pW, pH));
            }
            finally
            {
                _previewBitmap.Unlock();
            }
        }

        // Downscale current full-resolution mask into preview mask buffer
        private void SyncFullMaskToPreview()
        {
            if (_maskLayer == null || _maskLayerPreview == null || _maskOnlyBitmap == null) return;
            
            int pW = _maskLayerPreview.Width;
            int pH = _maskLayerPreview.Height;
            double invScale = 1.0 / PreviewScale;
            
            // Sync mask layer data
            for (int y = 0; y < pH; y++)
            {
                int srcY = (int)Math.Min(_maskLayer.Height - 1, Math.Round(y * invScale));
                for (int x = 0; x < pW; x++)
                {
                    int srcX = (int)Math.Min(_maskLayer.Width - 1, Math.Round(x * invScale));
                    _maskLayerPreview[x, y] = _maskLayer[srcX, srcY];
                }
            }
            
            // Also sync to overlay bitmap
            _maskOnlyBitmap.Lock();
            try
            {
                int stride = _maskOnlyBitmap.BackBufferStride;
                for (int y = 0; y < pH; y++)
                {
                    IntPtr rowPtr = _maskOnlyBitmap.BackBuffer + y * stride;
                    for (int x = 0; x < pW; x++)
                    {
                        var maskPixel = _maskLayerPreview[x, y];
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 0, maskPixel.B);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 1, maskPixel.G);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 2, maskPixel.R);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 3, maskPixel.A);
                        rowPtr += 4;
                    }
                }
                _maskOnlyBitmap.AddDirtyRect(new Int32Rect(0, 0, pW, pH));
            }
            finally
            {
                _maskOnlyBitmap.Unlock();
            }
        }

        private void DrawAtPointPreview(WpfPoint point, bool refresh = true)
        {
            if (_maskLayerPreview == null || _maskOnlyBitmap == null) return;

            int centerX = (int)Math.Round(point.X * PreviewScale);
            int centerY = (int)Math.Round(point.Y * PreviewScale);
            int radius = Math.Max(1, (int)Math.Round((_brushSize / 2.0) * PreviewScale));

            int minX = centerX - radius;
            int minY = centerY - radius;
            int maxX = centerX + radius;
            int maxY = centerY + radius;

            // Update mask layer data
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y <= radius * radius)
                    {
                        int px = centerX + x;
                        int py = centerY + y;
                        if (px >= 0 && px < _maskLayerPreview.Width && py >= 0 && py < _maskLayerPreview.Height)
                        {
                            _maskLayerPreview[px, py] = _isPaintMode
                                ? new Rgba32(_maskColor.R, _maskColor.G, _maskColor.B, _maskColor.A)
                                : new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            }

            // Update display immediately for instant feedback
            if (!refresh) return;

            int x0 = Math.Max(0, minX);
            int y0 = Math.Max(0, minY);
            int x1 = Math.Min(_maskLayerPreview.Width, maxX + 1);
            int y1 = Math.Min(_maskLayerPreview.Height, maxY + 1);

            if (x0 >= x1 || y0 >= y1) return;

            // Write ONLY mask pixels to mask-only bitmap - updates immediately!
            _maskOnlyBitmap.Lock();
            try
            {
                int stride = _maskOnlyBitmap.BackBufferStride;
                for (int y = y0; y < y1; y++)
                {
                    IntPtr rowPtr = _maskOnlyBitmap.BackBuffer + y * stride + x0 * 4;
                    for (int x = x0; x < x1; x++)
                    {
                        var maskPixel = _maskLayerPreview[x, y];
                        // Write mask color with alpha - WPF will composite via GPU
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 0, maskPixel.B);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 1, maskPixel.G);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 2, maskPixel.R);
                        System.Runtime.InteropServices.Marshal.WriteByte(rowPtr, 3, maskPixel.A);
                        rowPtr += 4;
                    }
                }
                _maskOnlyBitmap.AddDirtyRect(new Int32Rect(x0, y0, x1 - x0, y1 - y0));
            }
            finally
            {
                _maskOnlyBitmap.Unlock();
            }
        }

        /// <summary>
        /// Mouse move event - continue drawing
        /// </summary>
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;
            WpfPoint currentPoint = e.GetPosition(ImageCanvas);
            
            // Draw preview immediately
            DrawLinePreview(_lastPoint, currentPoint);
            
            // ALSO draw at full resolution incrementally (no lag on mouse up!)
            DrawLineFullRes(_lastPoint, currentPoint);
            
            _strokePoints.Add(currentPoint);
            _lastPoint = currentPoint;
        }

        /// <summary>
        /// Mouse up event - stop drawing
        /// </summary>
        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            ImageCanvas.ReleaseMouseCapture();

            // Switch back to working resolution display
            if (_previewMaskImage != null)
            {
                _previewMaskImage.Visibility = Visibility.Collapsed;
            }
            
            if (_previewBitmap != null && DisplayImage.Source == _previewBitmap)
            {
                DisplayImage.RenderTransform = Transform.Identity;
                DisplayImage.Source = _displayBitmap;
            }
            
            // Update the working resolution display with new mask
            UpdateDisplay();

            _accDirty = null;
            _strokePoints.Clear();
        }

        /// <summary>
        /// Preview line drawing (downscaled)
        /// </summary>
        private void DrawLinePreview(WpfPoint start, WpfPoint end)
        {
            int x0 = (int)start.X;
            int y0 = (int)start.Y;
            int x1 = (int)end.X;
            int y1 = (int)end.Y;

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawAtPointPreview(new WpfPoint(x0, y0), true);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>
        /// Full-res line drawing (Bresenham) used to replay preview stroke onto the main mask.
        /// </summary>
        private void DrawLineFullRes(WpfPoint start, WpfPoint end)
        {
            int x0 = (int)start.X;
            int y0 = (int)start.Y;
            int x1 = (int)end.X;
            int y1 = (int)end.Y;
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                DrawAtPoint(new WpfPoint(x0, y0), false);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>
        /// Draws at a specific point with current brush settings (full-res mask)
        /// </summary>
        private void DrawAtPoint(WpfPoint point, bool refresh = true)
        {
            int centerX = (int)point.X;
            int centerY = (int)point.Y;
            int radius = _brushSize / 2;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y <= radius * radius)
                    {
                        int px = centerX + x;
                        int py = centerY + y;
                        if (px >= 0 && px < _maskLayer.Width && py >= 0 && py < _maskLayer.Height)
                        {
                            _maskLayer[px, py] = _isPaintMode
                                ? new Rgba32(_maskColor.R, _maskColor.G, _maskColor.B, _maskColor.A)
                                : new Rgba32(0, 0, 0, 0);
                        }
                    }
                }
            }

            if (refresh)
            {
                UpdateDisplay(new Int32Rect(centerX - radius, centerY - radius, _brushSize, _brushSize));
            }
        }

        /// <summary>
        /// Saves current mask state for undo
        /// </summary>
        private void PushUndo()
        {
            if (_undoStack.Count >= MAX_UNDO_STEPS)
            {
                // Remove oldest
                var oldest = _undoStack.ToArray()[_undoStack.Count - 1];
                oldest.Dispose();
                
                var tempStack = new Stack<Image<Rgba32>>();
                for (int i = 0; i < MAX_UNDO_STEPS - 1; i++)
                {
                    tempStack.Push(_undoStack.Pop());
                }
                _undoStack.Clear();
                while (tempStack.Count > 0)
                {
                    _undoStack.Push(tempStack.Pop());
                }
            }

            _undoStack.Push(_maskLayer.Clone());
        }

        /// <summary>
        /// Color preview click - open color picker
        /// </summary>
        private void ColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = _maskColor,
                FullOpen = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Always use full opacity (no alpha channel)
                _maskColor = SD.Color.FromArgb(
                    255, // Always fully opaque
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B);
                
                UpdateColorPreview();
                UpdateCursorVisual();
            }
        }

        /// <summary>
        /// Clear mask button
        /// </summary>
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear the entire mask?",
                "Clear Mask",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                PushUndo();
                ClearMask();
                
                // Update working resolution display
                UpdateDisplay();
                
                // If in preview mode, also sync to preview
                if (_previewMaskImage != null && _previewMaskImage.Visibility == Visibility.Visible)
                {
                    SyncFullMaskToPreview();
                }
            }
        }

        /// <summary>
        /// Clears the mask layer
        /// </summary>
        private void ClearMask()
        {
            for (int y = 0; y < _maskLayer.Height; y++)
            {
                for (int x = 0; x < _maskLayer.Width; x++)
                {
                    _maskLayer[x, y] = new Rgba32(0, 0, 0, 0);
                }
            }
        }

        /// <summary>
        /// Undo button
        /// </summary>
        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                _maskLayer?.Dispose();
                _maskLayer = _undoStack.Pop();
                
                // Update working resolution display
                UpdateDisplay();
                
                // If in preview mode, also sync to preview
                if (_previewMaskImage != null && _previewMaskImage.Visibility == Visibility.Visible)
                {
                    SyncFullMaskToPreview();
                }
            }
        }

        /// <summary>
        /// Save button - apply changes and close
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Hide mask overlay before final compositing
            if (_previewMaskImage != null)
            {
                _previewMaskImage.Visibility = Visibility.Collapsed;
            }

            // Switch back to full-res display if in preview mode
            if (_previewBitmap != null && DisplayImage.Source == _previewBitmap)
            {
                DisplayImage.RenderTransform = Transform.Identity;
                DisplayImage.Source = _displayBitmap;
            }

            // Scale up mask to original resolution if needed
            Image<Rgba32> finalMask;
            if (_workingScale < 1.0)
            {
                // Scale up mask to original size
                finalMask = _maskLayer.Clone(ctx => ctx.Resize(
                    _originalImage.Width, 
                    _originalImage.Height, 
                    KnownResamplers.NearestNeighbor));
            }
            else
            {
                finalMask = _maskLayer.Clone();
            }

            // Update component with scaled-up mask
            _component.UpdateMask(finalMask);
            
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Cancel button - discard changes and close
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Hide mask overlay on cancel
            if (_previewMaskImage != null)
            {
                _previewMaskImage.Visibility = Visibility.Collapsed;
            }

            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _originalImage?.Dispose();
            _workingImage?.Dispose();
            _originalImagePreview?.Dispose();
            
            if (DialogResult != true)
            {
                _maskLayer?.Dispose();
                _maskLayerPreview?.Dispose();
            }
            
            foreach (var undo in _undoStack)
            {
                undo?.Dispose();
            }
            _undoStack.Clear();

            base.OnClosed(e);
        }

        private void ImageCanvas_MouseDown_ForPan(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed || (e.RightButton == MouseButtonState.Pressed && !_isDrawing))
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(this);
                ImageCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ImageCanvas_MouseUp_ForPan(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ImageCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void ImageCanvas_MouseMove_ForPan(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var pos = e.GetPosition(this);
                var dx = pos.X - _lastPanPoint.X;
                var dy = pos.Y - _lastPanPoint.Y;
                _translateTransform.X += dx;
                _translateTransform.Y += dy;
                _lastPanPoint = pos;
                e.Handled = true;
            }
        }

        private T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            DependencyObject? current = start;
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
