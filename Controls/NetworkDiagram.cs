using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VLSMCalculator.Models;

namespace VLSMCalculator.Controls;

public class NetworkDiagram : Canvas
{
    #region Dependency Properties
    public static readonly DependencyProperty SubnetsProperty =
        DependencyProperty.Register(nameof(Subnets), typeof(ObservableCollection<SubnetInfo>), 
            typeof(NetworkDiagram), new PropertyMetadata(null, OnSubnetsChanged));

    public static readonly DependencyProperty BaseNetworkProperty =
        DependencyProperty.Register(nameof(BaseNetwork), typeof(string), 
            typeof(NetworkDiagram), new PropertyMetadata("", OnBaseNetworkChanged));

    public ObservableCollection<SubnetInfo>? Subnets
    {
        get => (ObservableCollection<SubnetInfo>?)GetValue(SubnetsProperty);
        set => SetValue(SubnetsProperty, value);
    }

    public string BaseNetwork
    {
        get => (string)GetValue(BaseNetworkProperty);
        set => SetValue(BaseNetworkProperty, value);    }
    #endregion
    
    #region Fields & Types
    private Border? _hoveredSubnet, _draggedSubnet;
    private bool _isDragging, _isDrawing, _isDecelerationActive, _isHighPerformanceMode;
    private Point _dragStartPoint;
    private int _drawRequestSequence;
    private readonly List<SubnetNodeInfo> _subnetNodes = new();
    private readonly Dictionary<Border, Line> _connectionLines = new();
    private readonly Dictionary<Border, Storyboard> _activeAnimations = new();
    private readonly Dictionary<string, SolidColorBrush> _cachedBrushes = new();
    private readonly object _drawLock = new(), _animationLock = new();
    private System.Threading.CancellationTokenSource? _currentDrawCancellation;    private const int PerformanceThreshold = 15;
      // Enhanced responsive design constants
    private const double MinFontSize = 10.0;
    private const double MaxFontSize = 18.0;
    private const double MinContainerWidth = 170.0;  // Increased from 140.0
    private const double MaxContainerWidth = 300.0;  // Increased from 260.0  
    private const double MinContainerHeight = 120.0; // Increased from 80.0
    private const double MaxContainerHeight = 180.0; // Increased from 150.0
    private const double BaseCanvasSize = 800.0; // Reference size for scaling
    private const double DefaultTextPadding = 20.0; // Increased from 16.0

    private record SubnetNodeInfo
    {
        public Border Container { get; init; } = null!;
        public SubnetInfo Subnet { get; init; } = null!;
        public Point Position { get; set; }
        public Point Velocity { get; set; }
        public bool IsDecelerating { get; set; }
        public DateTime LastMoveTime { get; set; }
    }
    #endregion

    #region Event Handlers & Drawing
    private static void OnSubnetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NetworkDiagram)d).RequestDrawWithRaceConditionHandling();

    private static void OnBaseNetworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NetworkDiagram)d).RequestDrawWithRaceConditionHandling();

    private void RequestDrawWithRaceConditionHandling()
    {
        lock (_drawLock)
        {
            _currentDrawCancellation?.Cancel();
            _currentDrawCancellation = new();
            
            var currentSequence = ++_drawRequestSequence;
            var cancellationToken = _currentDrawCancellation.Token;
            var debounceDelay = Subnets?.Count > PerformanceThreshold ? 200 : 100;
            
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(debounceDelay, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested && currentSequence == _drawRequestSequence)
                        DrawDiagram();
                }
                catch (System.Threading.Tasks.TaskCanceledException) { }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (ActualWidth > 0 && ActualHeight > 0) RequestDrawWithRaceConditionHandling();
    }    private void DrawDiagram()
    {
        lock (_drawLock)
        {
            if (_isDrawing) return;
            _isDrawing = true;
        }        try        {
            // Debug information
            System.Diagnostics.Debug.WriteLine($"DrawDiagram called: Subnets={Subnets?.Count ?? -1}, ActualWidth={ActualWidth}, ActualHeight={ActualHeight}, BaseNetwork='{BaseNetwork}'");
            
            _isHighPerformanceMode = Subnets?.Count > PerformanceThreshold;
            CleanupAllAnimations();
            
            Children.Clear();
            _subnetNodes.Clear();
            _connectionLines.Clear();
            StopDecelerationTimer();            // Fix the null check - Subnets can be null or have 0 count
            if (Subnets == null || Subnets.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0 || string.IsNullOrEmpty(BaseNetwork))
            {
                System.Diagnostics.Debug.WriteLine("DrawDiagram: Early return due to invalid state");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"DrawDiagram: Proceeding with {Subnets.Count} subnets, HighPerformance={_isHighPerformanceMode}");
            OptimizeRenderingForNetworkSize();

            if (_isHighPerformanceMode)
                DrawHighPerformanceNetworkDiagram();
            else
                DrawModernNetworkDiagram();
            
            System.Diagnostics.Debug.WriteLine($"DrawDiagram: Completed drawing, Children.Count={Children.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error drawing network diagram: {ex.Message}");
        }
        finally
        {
            lock (_drawLock) { _isDrawing = false; }
        }
    }
    
    #region Performance Optimization Methods
    private void CleanupAllAnimations()
    {
        lock (_animationLock)
        {
            foreach (var kvp in _activeAnimations)
            {
                kvp.Value.Stop();
                kvp.Value.Remove();
            }
            _activeAnimations.Clear();
        }
    }

    private void StopDecelerationTimer() => _isDecelerationActive = false;
    
    private SolidColorBrush GetCachedBrush(Color color)
    {
        var key = $"{color.R}-{color.G}-{color.B}-{color.A}";
        if (!_cachedBrushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _cachedBrushes[key] = brush;
        }
        return brush;
    }
    
    private void OptimizeRenderingForNetworkSize()
    {
        if (_isHighPerformanceMode)
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            CacheMode = new BitmapCache { RenderAtScale = 0.8 };
        }
        else
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            CacheMode = null;
        }    }
    #endregion
    
    #region Text Measurement and Responsive Utilities
    
    /// <summary>
    /// Measures the actual size of text with the given parameters
    /// </summary>
    private Size MeasureTextSize(string text, double fontSize, FontFamily fontFamily, FontWeight fontWeight = default)
    {
        if (string.IsNullOrEmpty(text)) return new Size(0, 0);
        
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, FontStyles.Normal, fontWeight == default ? FontWeights.Normal : fontWeight, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
            
        return new Size(formattedText.Width, formattedText.Height);
    }
      /// <summary>
    /// Calculates optimal font size based on canvas size and content count
    /// </summary>
    private double GetScaledFontSize(double baseFontSize, double canvasWidth, double canvasHeight)
    {
        var scaleFactor = Math.Min(canvasWidth, canvasHeight) / BaseCanvasSize;
        var contentDensityFactor = Subnets?.Count > 0 ? Math.Max(0.9, 1.0 - (Subnets.Count - 5) * 0.02) : 1.0; // Reduce font size slightly for dense networks
        var scaledSize = baseFontSize * Math.Max(0.75, Math.Min(1.4, scaleFactor)) * contentDensityFactor;
        return Math.Max(MinFontSize, Math.Min(MaxFontSize, scaledSize));    }
    
    /// <summary>
    /// Calculates optimal container size based on content and canvas dimensions
    /// </summary>
    private Size CalculateOptimalContainerSize(SubnetInfo subnet, double fontSize, FontFamily fontFamily)
    {
        // Use the actual font sizes that will be used in rendering
        var primaryFontSize = fontSize; // Network text (bold)
        var secondaryFontSize = GetScaledFontSize(11, ActualWidth, ActualHeight); // Hosts text
        var tertiaryFontSize = GetScaledFontSize(10, ActualWidth, ActualHeight); // Required and range text
        
        // Measure all text content that will be displayed with actual font sizes
        var networkSize = MeasureTextSize(subnet.Network, primaryFontSize, fontFamily, FontWeights.Bold);
        var hostsSize = MeasureTextSize($"{subnet.UsableHosts} hosts", secondaryFontSize, fontFamily);
        var requiredSize = MeasureTextSize($"Required: {subnet.RequiredHosts}", tertiaryFontSize, fontFamily);
        var rangeSize = MeasureTextSize($"{subnet.FirstHost} - {subnet.LastHost}", tertiaryFontSize, fontFamily);
          // Add extra safety margin for the range text since it's often the longest
        var rangeSizeWithSafety = new Size(rangeSize.Width * 1.1, rangeSize.Height);
          // Calculate required width with generous padding for readability  
        var maxTextWidth = Math.Max(networkSize.Width, Math.Max(hostsSize.Width, Math.Max(requiredSize.Width, rangeSizeWithSafety.Width)));
        
        // Add extra margin for Consolas font which needs more horizontal space
        var consolasAdjustment = fontFamily.Source.Contains("Consolas") ? 1.15 : 1.0;
        var requiredWidth = (maxTextWidth * consolasAdjustment) + (DefaultTextPadding * 3.5); // Increased padding multiplier for better safety
        
        // Calculate required height with proper line spacing
        var totalTextHeight = networkSize.Height + hostsSize.Height + requiredSize.Height + rangeSize.Height;
        var lineSpacing = fontSize * 0.5; // Increased line spacing
        var requiredHeight = totalTextHeight + (lineSpacing * 4) + (DefaultTextPadding * 2.0); // More vertical spacing
        
        // Apply scaling based on canvas size with better minimum thresholds
        var scaleFactor = Math.Min(ActualWidth, ActualHeight) / BaseCanvasSize;
        var scaleMultiplier = Math.Max(0.85, Math.Min(1.3, scaleFactor)); // Better scaling range
        
        requiredWidth *= scaleMultiplier;
        requiredHeight *= scaleMultiplier;
        
        // Constrain to min/max values with improved bounds
        var finalWidth = Math.Max(MinContainerWidth, Math.Min(MaxContainerWidth, requiredWidth));
        var finalHeight = Math.Max(MinContainerHeight, Math.Min(MaxContainerHeight, requiredHeight));
        
        return new Size(finalWidth, finalHeight);
    }
    
    /// <summary>
    /// Gets adaptive spacing based on canvas size and element count
    /// </summary>
    private double GetAdaptiveSpacing(double baseSpacing)
    {
        var scaleFactor = Math.Min(ActualWidth, ActualHeight) / BaseCanvasSize;
        return baseSpacing * Math.Max(0.5, Math.Min(1.5, scaleFactor));
    }
    
    /// <summary>
    /// Centers text within a container and ensures it doesn't exceed bounds
    /// </summary>
    private Point CalculateTextPosition(Size textSize, Size containerSize, Point containerPosition, double margin = 5)
    {
        var x = containerPosition.X + (containerSize.Width - textSize.Width) / 2;
        var y = containerPosition.Y + (containerSize.Height - textSize.Height) / 2;
        
        // Ensure text doesn't go outside container bounds
        x = Math.Max(containerPosition.X + margin, Math.Min(containerPosition.X + containerSize.Width - textSize.Width - margin, x));
        y = Math.Max(containerPosition.Y + margin, Math.Min(containerPosition.Y + containerSize.Height - textSize.Height - margin, y));
        
        return new Point(x, y);
    }
    
    /// <summary>
    /// Calculates optimal radius for network layout based on container sizes
    /// </summary>
    private double CalculateOptimalRadius(Size averageContainerSize)
    {
        var baseRadius = Math.Min(ActualWidth, ActualHeight) * 0.3;
        var containerAdjustment = Math.Max(averageContainerSize.Width, averageContainerSize.Height) / 2;
        
        // Ensure containers don't overlap and fit comfortably
        var minRadius = containerAdjustment * 1.5;
        
        return Math.Max(minRadius, baseRadius);    }
    
    #endregion
    
    #region High Performance Drawing
    private static readonly SolidColorBrush[] ColorPalette = {
        new(Color.FromRgb(99, 102, 241)), new(Color.FromRgb(16, 185, 129)),
        new(Color.FromRgb(245, 158, 11)), new(Color.FromRgb(139, 92, 246)),
        new(Color.FromRgb(236, 72, 153)), new(Color.FromRgb(20, 184, 166)),
        new(Color.FromRgb(251, 146, 60)), new(Color.FromRgb(34, 197, 94))
    };    private void DrawHighPerformanceNetworkDiagram()
    {
        System.Diagnostics.Debug.WriteLine($"DrawHighPerformanceNetworkDiagram called with {Subnets?.Count} subnets");
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        
        // Calculate optimal radius based on improved container sizes
        var sampleContainer = CalculateOptimalContainerSize(Subnets![0], GetScaledFontSize(12, ActualWidth, ActualHeight), new FontFamily("Consolas"));
        var baseRadius = Math.Min(ActualWidth, ActualHeight) * 0.32; // Slightly larger base radius
        var containerRadius = Math.Max(sampleContainer.Width, sampleContainer.Height) / 2;
        var radius = Math.Max(baseRadius, containerRadius * 1.8); // Ensure adequate spacing

        DrawSimpleMainRouter();

        var elementsToAdd = new List<UIElement>();

        for (int i = 0; i < Subnets!.Count; i++)
        {
            var subnet = Subnets[i];
            var angle = (2 * Math.PI * i) / Subnets.Count;
            var x = centerX + radius * Math.Cos(angle);
            var y = centerY + radius * Math.Sin(angle);

            var line = CreateSimpleConnectionLine(centerX, centerY, x, y);
            var subnetContainer = CreateSimpleSubnetNode(subnet, x, y, ColorPalette[i % ColorPalette.Length]);
            
            elementsToAdd.Add(line);
            elementsToAdd.Add(subnetContainer);

            _subnetNodes.Add(new SubnetNodeInfo
            {
                Container = subnetContainer,
                Subnet = subnet,
                Position = new Point(x, y),
                Velocity = new Point(0, 0),
                IsDecelerating = false,
                LastMoveTime = DateTime.Now
            });
            _connectionLines[subnetContainer] = line;
        }        foreach (var element in elementsToAdd) Children.Add(element);
        System.Diagnostics.Debug.WriteLine($"DrawHighPerformanceNetworkDiagram: Added {elementsToAdd.Count} elements to Children");
        DrawSimpleBaseNetworkLabel();
    }private void DrawSimpleMainRouter()
    {
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        const double routerSize = 50;
        const double routerRadius = routerSize / 2;

        var routerBg = new Ellipse
        {
            Width = routerSize,
            Height = routerSize,
            Fill = GetCachedBrush(Color.FromRgb(55, 65, 81)),
            Stroke = GetCachedBrush(Color.FromRgb(99, 102, 241)),
            StrokeThickness = 2
        };
        Canvas.SetLeft(routerBg, centerX - routerRadius);
        Canvas.SetTop(routerBg, centerY - routerRadius);
        Children.Add(routerBg);

        var routerIcon = new TextBlock
        {
            Text = "R",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };
        Canvas.SetLeft(routerIcon, centerX - 6);
        Canvas.SetTop(routerIcon, centerY - 9);
        Children.Add(routerIcon);
    }

    private Line CreateSimpleConnectionLine(double x1, double y1, double x2, double y2)
    {
        const double routerRadius = 25;
        const double subnetOffset = 40;
        
        var dx = x2 - x1;
        var dy = y2 - y1;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        
        var normalizedDx = dx / distance;
        var normalizedDy = dy / distance;
        
        return new Line
        {
            X1 = x1 + normalizedDx * routerRadius,
            Y1 = y1 + normalizedDy * routerRadius,
            X2 = x2 - normalizedDx * subnetOffset,
            Y2 = y2 - normalizedDy * subnetOffset,
            Stroke = GetCachedBrush(Color.FromRgb(100, 100, 100)),
            StrokeThickness = 1,
            Opacity = 0.6
        };
    }    private Border CreateSimpleSubnetNode(SubnetInfo subnet, double x, double y, Brush color)
    {
        // Calculate responsive font sizes with improved scaling
        var primaryFontSize = GetScaledFontSize(12, ActualWidth, ActualHeight); // Increased base font size
        var secondaryFontSize = GetScaledFontSize(10, ActualWidth, ActualHeight); // Increased base font size
        var fontFamily = new FontFamily("Consolas");
        
        // Calculate optimal container size based on content
        var containerSize = CalculateOptimalContainerSize(subnet, primaryFontSize, fontFamily);
        
        // For high-performance mode, still ensure minimum readable size
        containerSize.Width = Math.Max(120, containerSize.Width * 0.8); // Increased minimum width
        containerSize.Height = Math.Max(60, containerSize.Height * 0.8); // Increased minimum height
        
        var container = new Border
        {
            Width = containerSize.Width,
            Height = containerSize.Height,
            Background = GetCachedBrush(Color.FromArgb(180, 31, 41, 55)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
          var adaptiveMargin = Math.Max(10, GetAdaptiveSpacing(12)); // Increased minimum margin
        var contentStack = new StackPanel { Margin = new Thickness(adaptiveMargin) };
        
        // Network address with measured sizing and improved readability
        var networkText = new TextBlock
        {
            Text = subnet.Network,
            FontSize = primaryFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = fontFamily,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, adaptiveMargin * 0.3) // Add spacing between elements
        };
        contentStack.Children.Add(networkText);
        
        // Host count with appropriate sizing and improved contrast
        var hostsText = new TextBlock
        {
            Text = $"{subnet.UsableHosts} hosts",
            FontSize = secondaryFontSize,
            Foreground = GetCachedBrush(Color.FromRgb(220, 220, 220)), // Improved contrast
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 0, 0, adaptiveMargin * 0.2)
        };
        contentStack.Children.Add(hostsText);

        container.Child = contentStack;
        
        // Center the container properly based on its actual size
        Canvas.SetLeft(container, x - containerSize.Width / 2);
        Canvas.SetTop(container, y - containerSize.Height / 2);

        AddBasicInteractivity(container);
        return container;
    }

    private void AddBasicInteractivity(Border container)
    {
        container.MouseLeftButtonDown += SubnetNode_MouseLeftButtonDown;
        container.MouseMove += SubnetNode_MouseMove;
        container.MouseLeftButtonUp += SubnetNode_MouseLeftButtonUp;
    }    private void DrawSimpleBaseNetworkLabel()
    {
        var fontSize = GetScaledFontSize(11, ActualWidth, ActualHeight); // Increased base font size
        var fontFamily = new FontFamily("Consolas");
        
        // Measure text to ensure proper container sizing
        var labelText = $"Base: {BaseNetwork}";
        var textSize = MeasureTextSize(labelText, fontSize, fontFamily, FontWeights.SemiBold);
        
        // Calculate adaptive padding with better minimums
        var adaptivePadding = Math.Max(10, GetAdaptiveSpacing(12));
        
        var textBlock = new TextBlock
        {
            Text = labelText,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            FontFamily = fontFamily,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        
        var label = new Border
        {
            Background = GetCachedBrush(Color.FromArgb(160, 99, 102, 241)), // Improved opacity for better readability
            CornerRadius = new CornerRadius(6), // Slightly larger corner radius
            Padding = new Thickness(adaptivePadding, adaptivePadding / 2, adaptivePadding, adaptivePadding / 2),
            Child = textBlock
        };

        // Position with adaptive spacing and ensure it doesn't go off-screen
        var margin = Math.Max(12, GetAdaptiveSpacing(15));
        var labelWidth = textSize.Width + (adaptivePadding * 2);        var maxX = Math.Max(0, ActualWidth - labelWidth - margin);
        
        Canvas.SetLeft(label, Math.Min(margin, maxX));
        Canvas.SetTop(label, margin);
        Children.Add(label);
    }
    
    #endregion
    
    #region Modern Drawing (Enhanced Performance)
    
    private void DrawModernNetworkDiagram()
    {
        System.Diagnostics.Debug.WriteLine($"DrawModernNetworkDiagram called with {Subnets?.Count} subnets");
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        
        // Calculate optimal radius based on improved container sizes
        var sampleContainer = CalculateOptimalContainerSize(Subnets![0], GetScaledFontSize(13, ActualWidth, ActualHeight), new FontFamily("Consolas"));
        var baseRadius = Math.Min(ActualWidth, ActualHeight) * 0.35; // Larger base radius for modern mode
        var containerRadius = Math.Max(sampleContainer.Width, sampleContainer.Height) / 2;
        var radius = Math.Max(baseRadius, containerRadius * 2.0); // More generous spacing for larger containers

        DrawMainRouter(centerX, centerY);

        var elementsToAdd = new List<UIElement>();        for (int i = 0; i < Subnets!.Count; i++)
        {
            var subnet = Subnets[i];
            var angle = (2 * Math.PI * i) / Subnets.Count;
            var x = centerX + radius * Math.Cos(angle);
            var y = centerY + radius * Math.Sin(angle);

            System.Diagnostics.Debug.WriteLine($"Creating subnet node {i}: {subnet.Network} at ({x:F1}, {y:F1})");
              var line = DrawConnectionLine(centerX, centerY, x, y, i);
            var subnetContainer = DrawSubnetNode(subnet, x, y, ColorPalette[i % ColorPalette.Length], i);
            
            System.Diagnostics.Debug.WriteLine($"Subnet node {i} created - Line: {line != null}, Container: {subnetContainer != null}");
              if (line != null) elementsToAdd.Add(line);
            if (subnetContainer != null) elementsToAdd.Add(subnetContainer);

            if (subnetContainer != null)
            {
                _subnetNodes.Add(new SubnetNodeInfo
                {
                    Container = subnetContainer,
                    Subnet = subnet,
                    Position = new Point(x, y),
                    Velocity = new Point(0, 0),
                    IsDecelerating = false,
                    LastMoveTime = DateTime.Now
                });
                if (line != null)
                    _connectionLines[subnetContainer] = line;
            }
        }

        System.Diagnostics.Debug.WriteLine($"Adding {elementsToAdd.Count} elements to canvas");
        foreach (var element in elementsToAdd) 
        {
            Children.Add(element);
            System.Diagnostics.Debug.WriteLine($"Added element: {element.GetType().Name}");
        }
        DrawBaseNetworkLabel();
    }private void DrawMainRouter(double centerX, double centerY)
    {
        // Calculate responsive router size based on canvas dimensions with better scaling
        var baseRouterSize = 90; // Increased base size
        var scaleFactor = Math.Min(ActualWidth, ActualHeight) / BaseCanvasSize;
        var routerSize = Math.Max(70, Math.Min(140, baseRouterSize * scaleFactor)); // Better size range
        var routerRadius = routerSize / 2;

        var routerBg = new Ellipse
        {
            Width = routerSize,
            Height = routerSize,
            Fill = GetCachedBrush(Color.FromRgb(55, 65, 81)),
            Stroke = GetCachedBrush(Color.FromRgb(99, 102, 241)),
            StrokeThickness = Math.Max(2, 3 * scaleFactor),
            // Only apply shadow effect for small networks
            Effect = Subnets!.Count <= 6 ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                Opacity = 0.2,
                ShadowDepth = 3
            } : null
        };

        Canvas.SetLeft(routerBg, centerX - routerRadius);
        Canvas.SetTop(routerBg, centerY - routerRadius);
        Children.Add(routerBg);

        // Router icon with responsive sizing
        var iconFontSize = GetScaledFontSize(36, ActualWidth, ActualHeight); // Increased base size
        var routerIcon = new TextBlock
        {
            Text = "🌐",
            FontSize = iconFontSize,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        // Measure and center the icon properly with better positioning
        routerIcon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var iconWidth = routerIcon.DesiredSize.Width;
        var iconHeight = routerIcon.DesiredSize.Height;
        
        Canvas.SetLeft(routerIcon, centerX - iconWidth / 2);
        Canvas.SetTop(routerIcon, centerY - iconHeight / 2);
        Children.Add(routerIcon);

        // Router label with responsive sizing and proper measurement
        var labelFontSize = GetScaledFontSize(13, ActualWidth, ActualHeight); // Increased base size
        var routerLabel = new TextBlock
        {
            Text = "Main Router",
            FontSize = labelFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        // Measure and center the text properly with adaptive spacing
        routerLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = routerLabel.DesiredSize.Width;
        var labelSpacing = Math.Max(12, GetAdaptiveSpacing(15)); // Better minimum spacing
          Canvas.SetLeft(routerLabel, centerX - textWidth / 2);
        Canvas.SetTop(routerLabel, centerY + routerRadius + labelSpacing);
        Children.Add(routerLabel);

        // Simplified pulsing animation - only for very small networks
        if (Subnets!.Count <= 8)
        {
            var pulseAnimation = new DoubleAnimation
            {
                From = 0.9,
                To = 1.1,
                Duration = TimeSpan.FromSeconds(3),
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var scaleTransform = new ScaleTransform(1, 1, routerRadius, routerRadius);
            routerBg.RenderTransform = scaleTransform;
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
        }
    }    private Line DrawConnectionLine(double x1, double y1, double x2, double y2, int index)
    {
        System.Diagnostics.Debug.WriteLine($"DrawConnectionLine called: from ({x1:F1},{y1:F1}) to ({x2:F1},{y2:F1}), index={index}");
        
        const double routerRadius = 40; // Half of router size (80/2)
        
        // Calculate the direction vector from router center to subnet center
        var dx = x2 - x1;
        var dy = y2 - y1;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        
        // Normalize the direction vector
        var normalizedDx = dx / distance;
        var normalizedDy = dy / distance;
        
        // Calculate connection points at the edge of the router and subnet
        var routerEdgeX = x1 + normalizedDx * routerRadius;
        var routerEdgeY = y1 + normalizedDy * routerRadius;
        
        // For subnet, connect to the closest edge (approximate center offset for the subnet box)
        const double subnetOffset = 80; // Half width of subnet container
        var subnetEdgeX = x2 - normalizedDx * subnetOffset;
        var subnetEdgeY = y2 - normalizedDy * subnetOffset;

        var line = new Line
        {
            X1 = routerEdgeX,
            Y1 = routerEdgeY,
            X2 = subnetEdgeX,
            Y2 = subnetEdgeY,
            Stroke = GetCachedBrush(Color.FromRgb(75, 85, 99)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
            Opacity = 0.7
        };

        // Simplified line animation - only for small networks
        if (Subnets!.Count <= 8 && index < 4) // Limit animations for performance
        {
            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 0.7,
                Duration = TimeSpan.FromMilliseconds(300 + index * 50),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };            line.BeginAnimation(OpacityProperty, fadeAnimation);
        }
        
        System.Diagnostics.Debug.WriteLine($"DrawConnectionLine returning line: {line != null}");
        return line;
    }    private Border DrawSubnetNode(SubnetInfo subnet, double x, double y, Brush color, int index)
    {
        System.Diagnostics.Debug.WriteLine($"DrawSubnetNode called: subnet={subnet.Network}, position=({x:F1},{y:F1}), index={index}");
        
        // Calculate responsive font sizes with improved base sizes
        var primaryFontSize = GetScaledFontSize(13, ActualWidth, ActualHeight); // Increased base size
        var secondaryFontSize = GetScaledFontSize(11, ActualWidth, ActualHeight); // Increased base size
        var tertiaryFontSize = GetScaledFontSize(10, ActualWidth, ActualHeight); // Increased base size
        var fontFamily = new FontFamily("Consolas");
        
        // Calculate optimal container size based on content
        var containerSize = CalculateOptimalContainerSize(subnet, primaryFontSize, fontFamily);
        System.Diagnostics.Debug.WriteLine($"DrawSubnetNode: Container size calculated as {containerSize.Width}x{containerSize.Height}");
        
        // Create subnet container with responsive design
        var container = new Border
        {
            Width = containerSize.Width,
            Height = containerSize.Height,
            Background = GetCachedBrush(Color.FromArgb(200, 31, 41, 55)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = color,
            BorderThickness = new Thickness(2),
            // Simplified drop shadow - only for small networks
            Effect = Subnets!.Count <= 8 ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6,
                Opacity = 0.2,
                ShadowDepth = 3
            } : null
        };        // Content stack with adaptive margin and improved spacing
        var adaptiveMargin = Math.Max(15, GetAdaptiveSpacing(18)); // Increased minimum margin
        var contentStack = new StackPanel
        {
            Margin = new Thickness(adaptiveMargin, adaptiveMargin, adaptiveMargin, adaptiveMargin)
        };

        // Subnet header with icon and improved layout
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconSize = Math.Max(18, primaryFontSize * 1.3); // Increased icon size
        var icon = new Border
        {
            Width = iconSize,
            Height = iconSize,
            Background = color,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, adaptiveMargin * 0.8, 0)
        };
        
        var iconText = new TextBlock
        {
            Text = "🔌",
            FontSize = iconSize * 0.65, // Better icon scaling
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White
        };
        
        icon.Child = iconText;
        Grid.SetColumn(icon, 0);
        headerGrid.Children.Add(icon);

        var networkText = new TextBlock
        {
            Text = subnet.Network,
            FontSize = primaryFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = fontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(networkText, 1);
        headerGrid.Children.Add(networkText);

        contentStack.Children.Add(headerGrid);        // Hosts info with responsive sizing and improved spacing
        var lineSpacing = Math.Max(4, adaptiveMargin * 0.4); // Increased line spacing
        var hostsText = new TextBlock
        {
            Text = $"{subnet.UsableHosts} hosts",
            FontSize = secondaryFontSize,
            Foreground = GetCachedBrush(Color.FromRgb(180, 190, 200)), // Improved contrast
            Margin = new Thickness(0, lineSpacing, 0, 0),
            TextWrapping = TextWrapping.NoWrap
        };
        contentStack.Children.Add(hostsText);

        // Required hosts with better spacing
        var requiredText = new TextBlock
        {
            Text = $"Required: {subnet.RequiredHosts}",
            FontSize = tertiaryFontSize,
            Foreground = GetCachedBrush(Color.FromRgb(245, 158, 11)),
            Margin = new Thickness(0, lineSpacing * 0.8, 0, 0),
            TextWrapping = TextWrapping.NoWrap
        };
        contentStack.Children.Add(requiredText);

        // Host range with text trimming for long addresses and improved spacing
        var rangeText = new TextBlock
        {
            Text = $"{subnet.FirstHost} - {subnet.LastHost}",
            FontSize = tertiaryFontSize,
            Foreground = GetCachedBrush(Color.FromRgb(125, 211, 252)),
            FontFamily = fontFamily,
            Margin = new Thickness(0, lineSpacing, 0, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        contentStack.Children.Add(rangeText);

        container.Child = contentStack;

        // Center the container properly based on its actual size
        Canvas.SetLeft(container, x - containerSize.Width / 2);
        Canvas.SetTop(container, y - containerSize.Height / 2);

        // Simplified entrance animation - only for small networks
        if (Subnets!.Count <= 8)
        {
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(0.8, 0.8, containerSize.Width / 2, containerSize.Height / 2);
            transformGroup.Children.Add(scaleTransform);
            
            container.RenderTransform = transformGroup;
            container.Opacity = 0;

            // Combined scale and fade animation
            var scaleAnimation = new DoubleAnimation
            {
                From = 0.8,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                BeginTime = TimeSpan.FromMilliseconds(100 + index * 50),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                BeginTime = TimeSpan.FromMilliseconds(100 + index * 50)
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            container.BeginAnimation(OpacityProperty, fadeAnimation);
        }        // Add interactive functionality
        AddInteractiveEvents(container);

        System.Diagnostics.Debug.WriteLine($"DrawSubnetNode returning container: {container != null}, Width={container?.Width}, Height={container?.Height}");
        return container;
    }    private void DrawBaseNetworkLabel()
    {
        var fontSize = GetScaledFontSize(13, ActualWidth, ActualHeight); // Increased base font size
        var fontFamily = new FontFamily("Consolas");
        
        // Measure text to ensure proper container sizing
        var labelText = $"Base Network: {BaseNetwork}";
        var textSize = MeasureTextSize(labelText, fontSize, fontFamily, FontWeights.Bold);
        
        // Calculate adaptive padding and positioning with better minimums
        var adaptivePadding = Math.Max(14, GetAdaptiveSpacing(16)); // Better minimum padding
        var adaptiveMargin = Math.Max(20, GetAdaptiveSpacing(25)); // Better minimum margin
        
        var textBlock = new TextBlock
        {
            Text = labelText,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = fontFamily,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        
        var label = new Border
        {
            Background = GetCachedBrush(Color.FromArgb(190, 99, 102, 241)), // Improved opacity for better readability
            CornerRadius = new CornerRadius(10), // Larger corner radius for modern look
            Padding = new Thickness(adaptivePadding, adaptivePadding * 0.6, adaptivePadding, adaptivePadding * 0.6),
            Effect = Subnets!.Count <= 12 ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8,
                Opacity = 0.3,
                ShadowDepth = 3
            } : null,
            Child = textBlock
        };

        // Position with adaptive spacing and ensure it doesn't go off-screen
        var labelWidth = textSize.Width + (adaptivePadding * 2);
        var maxX = Math.Max(0, ActualWidth - labelWidth - adaptiveMargin);
        
        Canvas.SetLeft(label, Math.Min(adaptiveMargin, maxX));
        Canvas.SetTop(label, adaptiveMargin);
        Children.Add(label);

        // Add fade-in animation only for smaller networks
        if (Subnets!.Count <= 12)
        {
            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500),
                BeginTime = TimeSpan.FromMilliseconds(100)
            };

            label.BeginAnimation(OpacityProperty, fadeAnimation);
        }
    }

    #endregion

    #region Interactive Functionality

    private void AddInteractiveEvents(Border container)
    {
        container.MouseEnter += SubnetNode_MouseEnter;
        container.MouseLeave += SubnetNode_MouseLeave;
        container.MouseLeftButtonDown += SubnetNode_MouseLeftButtonDown;
        container.MouseMove += SubnetNode_MouseMove;
        container.MouseLeftButtonUp += SubnetNode_MouseLeftButtonUp;
        container.Cursor = Cursors.Hand;
    }

    private void SubnetNode_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border container && !_isDragging && !_isHighPerformanceMode)
        {
            _hoveredSubnet = container;
            ApplyHoverEffect(container, true);
            DimOtherNodes(container);
        }
    }

    private void SubnetNode_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border container && !_isDragging && !_isHighPerformanceMode)
        {
            _hoveredSubnet = null;
            ApplyHoverEffect(container, false);
            RestoreOtherNodes();
        }
    }

    private void SubnetNode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border container)
        {
            _isDragging = true;
            _draggedSubnet = container;
            _dragStartPoint = e.GetPosition(this);
            container.CaptureMouse();
            container.Cursor = Cursors.SizeAll;
            
            // Initialize velocity tracking
            var nodeInfo = _subnetNodes.FirstOrDefault(n => n.Container == container);
            if (nodeInfo != null)
            {
                nodeInfo.Velocity = new Point(0, 0);
                nodeInfo.LastMoveTime = DateTime.Now;
                nodeInfo.IsDecelerating = false;
            }
            
            e.Handled = true;
        }
    }

    private void SubnetNode_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _draggedSubnet != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var currentPosition = e.GetPosition(this);
            var deltaX = currentPosition.X - _dragStartPoint.X;
            var deltaY = currentPosition.Y - _dragStartPoint.Y;

            var currentLeft = Canvas.GetLeft(_draggedSubnet);
            var currentTop = Canvas.GetTop(_draggedSubnet);

            var newLeft = currentLeft + deltaX;
            var newTop = currentTop + deltaY;

            // Keep within bounds
            newLeft = Math.Max(0, Math.Min(ActualWidth - _draggedSubnet.Width, newLeft));
            newTop = Math.Max(0, Math.Min(ActualHeight - _draggedSubnet.Height, newTop));

            Canvas.SetLeft(_draggedSubnet, newLeft);
            Canvas.SetTop(_draggedSubnet, newTop);

            // Update node info and track velocity for deceleration
            var nodeInfo = _subnetNodes.FirstOrDefault(n => n.Container == _draggedSubnet);
            if (nodeInfo != null)
            {
                var currentTime = DateTime.Now;
                var timeDelta = (currentTime - nodeInfo.LastMoveTime).TotalMilliseconds;
                
                // Only calculate velocity if we have a reasonable time delta
                if (timeDelta > 5 && timeDelta < 100) // Between 5ms and 100ms
                {
                    // Calculate velocity in pixels per frame - increased multiplier for more pronounced effect
                    var velocityScale = 16.67 / timeDelta;
                    nodeInfo.Velocity = new Point(
                        deltaX * velocityScale * 4, // Increased from 2 to 4 for more noticeable effect
                        deltaY * velocityScale * 4
                    );
                }
                else if (timeDelta <= 5)
                {
                    // Very fast movement - use previous velocity but amplify it more
                    nodeInfo.Velocity = new Point(
                        nodeInfo.Velocity.X + deltaX * 1.0, // Increased from 0.5 to 1.0
                        nodeInfo.Velocity.Y + deltaY * 1.0
                    );
                }
                
                nodeInfo.Position = new Point(newLeft + _draggedSubnet.Width / 2, newTop + _draggedSubnet.Height / 2);
                nodeInfo.LastMoveTime = currentTime;
                nodeInfo.IsDecelerating = false;
                
                UpdateConnectionLine(_draggedSubnet);
            }

            _dragStartPoint = currentPosition;
            e.Handled = true;
        }
    }

    private void SubnetNode_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && _draggedSubnet != null)
        {
            _isDragging = false;
            _draggedSubnet.ReleaseMouseCapture();
            _draggedSubnet.Cursor = Cursors.Hand;
            
            // Use optimized deceleration instead of timer-based approach
            var nodeInfo = _subnetNodes.FirstOrDefault(n => n.Container == _draggedSubnet);
            if (nodeInfo != null && !_isHighPerformanceMode)
            {
                var totalVelocity = Math.Abs(nodeInfo.Velocity.X) + Math.Abs(nodeInfo.Velocity.Y);
                if (totalVelocity > 3.0) // Higher threshold for better performance
                {
                    // Scale down velocity for more controlled effect
                    nodeInfo.Velocity = new Point(
                        nodeInfo.Velocity.X * 0.4,
                        nodeInfo.Velocity.Y * 0.4
                    );
                    
                    nodeInfo.IsDecelerating = true;
                    StartOptimizedDeceleration(nodeInfo);
                }
                else
                {
                    // Stop immediately for precise placement
                    nodeInfo.Velocity = new Point(0, 0);
                    nodeInfo.IsDecelerating = false;
                }
            }
            
            _draggedSubnet = null;
            e.Handled = true;
        }
    }

    private void ApplyHoverEffect(Border container, bool isHovered)
    {
        // Skip hover effects for large networks to improve performance
        if (Subnets!.Count > 12) return;
        
        lock (_animationLock)
        {
            // Stop any existing animation for this container
            if (_activeAnimations.TryGetValue(container, out var existingStoryboard))
            {
                existingStoryboard.Stop();
                _activeAnimations.Remove(container);
            }
        }
        
        var scaleValue = isHovered ? 1.1 : 1.0; // Reduced scale for better performance
        var zIndex = isHovered ? 100 : 1;

        var transformGroup = container.RenderTransform as TransformGroup ?? new TransformGroup();
        var existingScale = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
        
        if (existingScale == null)
        {
            existingScale = new ScaleTransform(1, 1, container.Width / 2, container.Height / 2);
            transformGroup.Children.Add(existingScale);
            container.RenderTransform = transformGroup;
        }

        // Simplified animations
        var storyboard = new Storyboard();
        
        var scaleAnimationX = new DoubleAnimation
        {
            To = scaleValue,
            Duration = TimeSpan.FromMilliseconds(150), // Faster animation
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        var scaleAnimationY = new DoubleAnimation
        {
            To = scaleValue,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(scaleAnimationX, existingScale);
        Storyboard.SetTargetProperty(scaleAnimationX, new PropertyPath(ScaleTransform.ScaleXProperty));
        Storyboard.SetTarget(scaleAnimationY, existingScale);
        Storyboard.SetTargetProperty(scaleAnimationY, new PropertyPath(ScaleTransform.ScaleYProperty));
        
        storyboard.Children.Add(scaleAnimationX);
        storyboard.Children.Add(scaleAnimationY);

        // Z-index management
        Panel.SetZIndex(container, zIndex);

        // Simplified shadow effect - only for very small networks
        if (Subnets!.Count <= 6 && container.Effect is System.Windows.Media.Effects.DropShadowEffect shadowEffect)
        {
            var shadowAnimation = new DoubleAnimation
            {
                To = isHovered ? 12 : 6, // Reduced shadow blur
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            Storyboard.SetTarget(shadowAnimation, shadowEffect);
            Storyboard.SetTargetProperty(shadowAnimation, new PropertyPath(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty));
            storyboard.Children.Add(shadowAnimation);
        }
        
        lock (_animationLock)
        {
            _activeAnimations[container] = storyboard;
        }
        
        storyboard.Completed += (s, e) =>
        {
            lock (_animationLock)
            {
                _activeAnimations.Remove(container);
            }
        };
        
        storyboard.Begin();
    }

    private void DimOtherNodes(Border hoveredContainer)
    {
        // Skip dimming for large networks to improve performance
        if (Subnets!.Count > 12) return;
        
        foreach (var nodeInfo in _subnetNodes)
        {
            if (nodeInfo.Container != hoveredContainer)
            {
                var dimAnimation = new DoubleAnimation
                {
                    To = 0.5,
                    Duration = TimeSpan.FromMilliseconds(100) // Faster animation
                };
                nodeInfo.Container.BeginAnimation(OpacityProperty, dimAnimation);
            }
        }

        // Dim connection lines
        foreach (var line in _connectionLines.Values)
        {
            if (_connectionLines.FirstOrDefault(kvp => kvp.Key == hoveredContainer).Value != line)
            {
                var dimAnimation = new DoubleAnimation
                {
                    To = 0.3,
                    Duration = TimeSpan.FromMilliseconds(100)
                };
                line.BeginAnimation(OpacityProperty, dimAnimation);
            }
        }
    }

    private void RestoreOtherNodes()
    {
        // Skip restore for large networks to improve performance
        if (Subnets!.Count > 12) return;
        
        foreach (var nodeInfo in _subnetNodes)
        {
            var restoreAnimation = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(100) // Faster animation
            };
            nodeInfo.Container.BeginAnimation(OpacityProperty, restoreAnimation);
        }

        // Restore connection lines
        foreach (var line in _connectionLines.Values)
        {
            var restoreAnimation = new DoubleAnimation
            {
                To = 0.7,
                Duration = TimeSpan.FromMilliseconds(100)
            };
            line.BeginAnimation(OpacityProperty, restoreAnimation);
        }
    }

    private void UpdateConnectionLine(Border container)
    {
        if (_connectionLines.TryGetValue(container, out var line))
        {
            var centerX = ActualWidth / 2;
            var centerY = ActualHeight / 2;
            const double routerRadius = 40;

            var containerCenterX = Canvas.GetLeft(container) + container.Width / 2;
            var containerCenterY = Canvas.GetTop(container) + container.Height / 2;

            var dx = containerCenterX - centerX;
            var dy = containerCenterY - centerY;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            var normalizedDx = dx / distance;
            var normalizedDy = dy / distance;

            var routerEdgeX = centerX + normalizedDx * routerRadius;
            var routerEdgeY = centerY + normalizedDy * routerRadius;

            const double subnetOffset = 80;
            var subnetEdgeX = containerCenterX - normalizedDx * subnetOffset;
            var subnetEdgeY = containerCenterY - normalizedDy * subnetOffset;

            line.X1 = routerEdgeX;
            line.Y1 = routerEdgeY;
            line.X2 = subnetEdgeX;
            line.Y2 = subnetEdgeY;
        }
    }

    private void StartOptimizedDeceleration(SubnetNodeInfo nodeInfo)
    {
        if (_isDecelerationActive) return;
        
        _isDecelerationActive = true;
        
        // Use composition animation for better performance
        var storyboard = new Storyboard();
        
        // Create deceleration animation
        var duration = TimeSpan.FromMilliseconds(800); // Fixed duration for predictable performance
        var ease = new PowerEase { EasingMode = EasingMode.EaseOut, Power = 3 };
        
        var transformGroup = nodeInfo.Container.RenderTransform as TransformGroup ?? new TransformGroup();
        var translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
        
        if (translateTransform == null)
        {
            translateTransform = new TranslateTransform();
            transformGroup.Children.Add(translateTransform);
            nodeInfo.Container.RenderTransform = transformGroup;
        }
        
        var animX = new DoubleAnimation
        {
            From = 0,
            To = nodeInfo.Velocity.X * 50, // Scale velocity for visual effect
            Duration = duration,
            EasingFunction = ease
        };
        
        var animY = new DoubleAnimation
        {
            From = 0,
            To = nodeInfo.Velocity.Y * 50,
            Duration = duration,
            EasingFunction = ease
        };
        
        Storyboard.SetTarget(animX, translateTransform);
        Storyboard.SetTargetProperty(animX, new PropertyPath(TranslateTransform.XProperty));
        Storyboard.SetTarget(animY, translateTransform);
        Storyboard.SetTargetProperty(animY, new PropertyPath(TranslateTransform.YProperty));
        
        storyboard.Children.Add(animX);
        storyboard.Children.Add(animY);
        
        storyboard.Completed += (s, e) =>
        {
            _isDecelerationActive = false;
            nodeInfo.IsDecelerating = false;
            
            // Reset transform
            translateTransform.X = 0;
            translateTransform.Y = 0;
            
            // Update final position
            var finalX = Canvas.GetLeft(nodeInfo.Container) + nodeInfo.Velocity.X * 50;
            var finalY = Canvas.GetTop(nodeInfo.Container) + nodeInfo.Velocity.Y * 50;
            
            // Keep within bounds
            finalX = Math.Max(0, Math.Min(ActualWidth - nodeInfo.Container.Width, finalX));
            finalY = Math.Max(0, Math.Min(ActualHeight - nodeInfo.Container.Height, finalY));
            
            Canvas.SetLeft(nodeInfo.Container, finalX);
            Canvas.SetTop(nodeInfo.Container, finalY);
            
            UpdateConnectionLine(nodeInfo.Container);
              lock (_animationLock)
            {
                _activeAnimations.Remove(nodeInfo.Container);
            }
        };
          lock (_animationLock)
        {
            _activeAnimations[nodeInfo.Container] = storyboard;
        }
        
        storyboard.Begin();
    }

    #endregion

    public NetworkDiagram()
    {
        Loaded += NetworkDiagram_Loaded;
        Unloaded += NetworkDiagram_Unloaded;
    }

    private void NetworkDiagram_Loaded(object sender, RoutedEventArgs e)
    {
        // Control is loaded and ready
        // Enable hardware acceleration for better performance
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    private void NetworkDiagram_Unloaded(object sender, RoutedEventArgs e)
    {
        // Clean up resources
        StopDecelerationTimer();
        CleanupAllAnimations();        _currentDrawCancellation?.Cancel();
        _currentDrawCancellation?.Dispose();
        _currentDrawCancellation = null;
        
        // Clear caches
        _cachedBrushes.Clear();
    }
    #endregion
}
