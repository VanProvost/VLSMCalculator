using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VLSMCalculator.Models;
using System.Threading;
using System.Threading.Tasks;

namespace VLSMCalculator.Controls;

public class NetworkDiagram : Canvas
{
    public static readonly DependencyProperty SubnetsProperty =
        DependencyProperty.Register(nameof(Subnets), typeof(ObservableCollection<SubnetInfo>), 
            typeof(NetworkDiagram), new PropertyMetadata(null, OnSubnetsChanged));

    public static readonly DependencyProperty BaseNetworkProperty =
        DependencyProperty.Register(nameof(BaseNetwork), typeof(string), 
            typeof(NetworkDiagram), new PropertyMetadata("", OnBaseNetworkChanged));

    // Interactive state tracking
    private Border? _hoveredSubnet;
    private bool _isDragging;
    private Point _dragStartPoint;
    private Border? _draggedSubnet;
    private readonly List<SubnetNodeInfo> _subnetNodes = new();
    private readonly Dictionary<Border, Line> _connectionLines = new();
    private System.Windows.Threading.DispatcherTimer? _forceTimer;
    
    // Race condition handling
    private bool _isDrawing;
    private int _drawRequestSequence;
    private readonly object _drawLock = new();
    private System.Threading.CancellationTokenSource? _currentDrawCancellation;
    
    // Performance optimizations
    private readonly Dictionary<Border, Storyboard> _activeAnimations = new();
    private readonly HashSet<Border> _animatingNodes = new();
    private bool _isDecelerationActive = false;
    private readonly object _animationLock = new();
    
    // Performance thresholds and caching
    private readonly int _performanceThreshold = 15; // Switch to high perf mode above this count
    private readonly Dictionary<string, SolidColorBrush> _cachedBrushes = new();
    private bool _isHighPerformanceMode = false;

    public ObservableCollection<SubnetInfo>? Subnets
    {
        get => (ObservableCollection<SubnetInfo>?)GetValue(SubnetsProperty);
        set => SetValue(SubnetsProperty, value);
    }

    public string BaseNetwork
    {
        get => (string)GetValue(BaseNetworkProperty);
        set => SetValue(BaseNetworkProperty, value);
    }

    private class SubnetNodeInfo
    {
        public Border Container { get; set; } = null!;
        public SubnetInfo Subnet { get; set; } = null!;
        public Point Position { get; set; }
        public Point Velocity { get; set; }
        public bool IsDecelerating { get; set; }
        public DateTime LastMoveTime { get; set; }
    }

    private static void OnSubnetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NetworkDiagram diagram)
        {
            diagram.RequestDrawWithRaceConditionHandling();
        }
    }

    private static void OnBaseNetworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NetworkDiagram diagram)
        {
            diagram.RequestDrawWithRaceConditionHandling();
        }
    }

    private void RequestDrawWithRaceConditionHandling()
    {
        lock (_drawLock)
        {
            // Cancel any pending draw operation
            _currentDrawCancellation?.Cancel();
            _currentDrawCancellation = new System.Threading.CancellationTokenSource();
            
            var currentSequence = ++_drawRequestSequence;
            var cancellationToken = _currentDrawCancellation.Token;

            // Adaptive debounce delay based on network size for better performance
            var debounceDelay = Subnets?.Count > _performanceThreshold ? 200 : 100;
            
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(debounceDelay, cancellationToken);
                    
                    if (cancellationToken.IsCancellationRequested || currentSequence != _drawRequestSequence)
                        return;

                    DrawDiagram();
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // Expected when cancellation is requested
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Use enhanced race condition handling for size changes
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            RequestDrawWithRaceConditionHandling();
        }
    }

    private void DrawDiagram()
    {
        lock (_drawLock)
        {
            if (_isDrawing) return;
            _isDrawing = true;
        }

        try
        {
            // Determine performance mode based on network count
            _isHighPerformanceMode = Subnets?.Count > _performanceThreshold;
            
            // Clean up existing animations before clearing
            CleanupAllAnimations();
            
            // Clear existing children
            Children.Clear();
            _subnetNodes.Clear();
            _connectionLines.Clear();

            // Stop force timer if running
            StopDecelerationTimer();

            // Validate that we have the necessary data and valid dimensions
            if (Subnets == null || Subnets.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            // Ensure BaseNetwork is not null
            if (string.IsNullOrEmpty(BaseNetwork))
            {
                return;
            }

            // Optimize rendering settings based on network size
            OptimizeRenderingForNetworkSize();

            // Use appropriate drawing method
            if (_isHighPerformanceMode)
            {
                DrawHighPerformanceNetworkDiagram();
            }
            else
            {
                DrawModernNetworkDiagram();
            }
        }
        catch (Exception ex)
        {
            // Log error if needed, but don't crash the UI
            System.Diagnostics.Debug.WriteLine($"Error drawing network diagram: {ex.Message}");
        }
        finally
        {
            lock (_drawLock)
            {
                _isDrawing = false;
            }
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
            _animatingNodes.Clear();
        }
    }
    
    private void StopDecelerationTimer()
    {
        if (_forceTimer != null)
        {
            _forceTimer.Stop();
            _forceTimer = null;
        }
        _isDecelerationActive = false;
    }
    
    private SolidColorBrush GetCachedBrush(Color color)
    {
        var key = $"{color.R}-{color.G}-{color.B}-{color.A}";
        if (!_cachedBrushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze(); // Freeze for better performance
            _cachedBrushes[key] = brush;
        }
        return brush;
    }
    
    private void OptimizeRenderingForNetworkSize()
    {
        // Enable hardware acceleration optimizations based on network size
        if (_isHighPerformanceMode)
        {
            // For large networks, prioritize performance over visual quality
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            CacheMode = new BitmapCache { RenderAtScale = 0.8 };
        }
        else
        {
            // For smaller networks, maintain high visual quality
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            CacheMode = null;
        }
    }
    
    #endregion

    #region High Performance Drawing

    private void DrawHighPerformanceNetworkDiagram()
    {
        // Simplified drawing for large networks - minimal visual effects for better performance
        var colors = new[]
        {
            GetCachedBrush(Color.FromRgb(99, 102, 241)),
            GetCachedBrush(Color.FromRgb(16, 185, 129)),
            GetCachedBrush(Color.FromRgb(245, 158, 11)),
            GetCachedBrush(Color.FromRgb(139, 92, 246)),
            GetCachedBrush(Color.FromRgb(236, 72, 153)),
            GetCachedBrush(Color.FromRgb(20, 184, 166)),
            GetCachedBrush(Color.FromRgb(251, 146, 60)),
            GetCachedBrush(Color.FromRgb(34, 197, 94))
        };

        // Draw simplified main router
        DrawSimpleMainRouter();

        // Calculate positions
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        var radius = Math.Min(ActualWidth, ActualHeight) * 0.3;

        // Batch creation for better performance
        var elementsToAdd = new List<UIElement>();

        for (int i = 0; i < Subnets!.Count; i++)
        {
            var subnet = Subnets[i];
            var angle = (2 * Math.PI * i) / Subnets.Count;
            var x = centerX + radius * Math.Cos(angle);
            var y = centerY + radius * Math.Sin(angle);

            // Simplified connection line (no animation)
            var line = CreateSimpleConnectionLine(centerX, centerY, x, y);
            elementsToAdd.Add(line);

            // Simplified subnet node (minimal styling)
            var subnetContainer = CreateSimpleSubnetNode(subnet, x, y, colors[i % colors.Length]);
            elementsToAdd.Add(subnetContainer);

            // Store basic node info
            var nodeInfo = new SubnetNodeInfo
            {
                Container = subnetContainer,
                Subnet = subnet,
                Position = new Point(x, y),
                Velocity = new Point(0, 0),
                IsDecelerating = false,
                LastMoveTime = DateTime.Now
            };
            _subnetNodes.Add(nodeInfo);
            _connectionLines[subnetContainer] = line;
        }

        // Add all elements at once
        foreach (var element in elementsToAdd)
        {
            Children.Add(element);
        }

        // Simple base network label
        DrawSimpleBaseNetworkLabel();
    }

    private void DrawSimpleMainRouter()
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
    }

    private Border CreateSimpleSubnetNode(SubnetInfo subnet, double x, double y, Brush color)
    {
        var container = new Border
        {
            Width = 110,
            Height = 60,
            Background = GetCachedBrush(Color.FromArgb(180, 31, 41, 55)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = color,
            BorderThickness = new Thickness(1)
        };

        var contentStack = new StackPanel
        {
            Margin = new Thickness(6, 4, 6, 4)
        };

        var networkText = new TextBlock
        {
            Text = subnet.Network,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        contentStack.Children.Add(networkText);

        var hostsText = new TextBlock
        {
            Text = $"{subnet.UsableHosts} hosts",
            FontSize = 8,
            Foreground = GetCachedBrush(Color.FromRgb(200, 200, 200)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        contentStack.Children.Add(hostsText);

        container.Child = contentStack;
        Canvas.SetLeft(container, x - 55);
        Canvas.SetTop(container, y - 30);

        // Add basic drag functionality only
        AddBasicInteractivity(container);

        return container;
    }

    private void AddBasicInteractivity(Border container)
    {
        container.MouseLeftButtonDown += SubnetNode_MouseLeftButtonDown;
        container.MouseMove += SubnetNode_MouseMove;
        container.MouseLeftButtonUp += SubnetNode_MouseLeftButtonUp;
        container.Cursor = Cursors.Hand;
    }

    private void DrawSimpleBaseNetworkLabel()
    {
        var label = new Border
        {
            Background = GetCachedBrush(Color.FromArgb(140, 99, 102, 241)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var labelText = new TextBlock
        {
            Text = $"Base: {BaseNetwork}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas")
        };

        label.Child = labelText;
        Canvas.SetLeft(label, 10);
        Canvas.SetTop(label, 10);
        Children.Add(label);
    }

    #endregion

    #region Modern Drawing (Enhanced Performance)

    private void DrawModernNetworkDiagram()
    {
        // Modern color palette with cached brushes
        var colors = new[]
        {
            GetCachedBrush(Color.FromRgb(99, 102, 241)),   // Primary
            GetCachedBrush(Color.FromRgb(16, 185, 129)),   // Secondary  
            GetCachedBrush(Color.FromRgb(245, 158, 11)),   // Accent
            GetCachedBrush(Color.FromRgb(139, 92, 246)),   // Purple
            GetCachedBrush(Color.FromRgb(236, 72, 153)),   // Pink
            GetCachedBrush(Color.FromRgb(20, 184, 166)),   // Teal
            GetCachedBrush(Color.FromRgb(251, 146, 60)),   // Orange
            GetCachedBrush(Color.FromRgb(34, 197, 94))     // Green
        };

        // Draw main router/switch in center
        DrawMainRouter();

        // Calculate positions for subnets in a circular layout
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        var radius = Math.Min(ActualWidth, ActualHeight) * 0.3;

        // Batch operations for better performance
        var elementsToAdd = new List<UIElement>();

        for (int i = 0; i < Subnets!.Count; i++)
        {
            var subnet = Subnets[i];
            var angle = (2 * Math.PI * i) / Subnets.Count;
            var x = centerX + radius * Math.Cos(angle);
            var y = centerY + radius * Math.Sin(angle);

            // Draw connection line with selective animation
            var line = DrawConnectionLine(centerX, centerY, x, y, i);
            elementsToAdd.Add(line);

            // Draw subnet node with optimized styling
            var subnetContainer = DrawSubnetNode(subnet, x, y, colors[i % colors.Length], i);
            elementsToAdd.Add(subnetContainer);

            // Store subnet node info for interactions
            var nodeInfo = new SubnetNodeInfo
            {
                Container = subnetContainer,
                Subnet = subnet,
                Position = new Point(x, y),
                Velocity = new Point(0, 0),
                IsDecelerating = false,
                LastMoveTime = DateTime.Now
            };
            _subnetNodes.Add(nodeInfo);
            
            // Store connection line for updates
            _connectionLines[subnetContainer] = line;
        }

        // Batch add all elements for better performance
        foreach (var element in elementsToAdd)
        {
            Children.Add(element);
        }

        // Draw base network label
        DrawBaseNetworkLabel();
    }

    private void DrawMainRouter()
    {
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        const double routerSize = 80;
        const double routerRadius = routerSize / 2;

        // Simplified router background - reduced effects for better performance
        var routerBg = new Ellipse
        {
            Width = routerSize,
            Height = routerSize,
            Fill = GetCachedBrush(Color.FromRgb(55, 65, 81)),
            Stroke = GetCachedBrush(Color.FromRgb(99, 102, 241)),
            StrokeThickness = 3,
            // Only apply shadow effect for very small networks
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

        // Router icon - centered properly
        var routerIcon = new TextBlock
        {
            Text = "🌐",
            FontSize = 32,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        // Measure the icon to center it properly
        routerIcon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var iconWidth = routerIcon.DesiredSize.Width;
        var iconHeight = routerIcon.DesiredSize.Height;
        
        Canvas.SetLeft(routerIcon, centerX - iconWidth / 2);
        Canvas.SetTop(routerIcon, centerY - iconHeight / 2);
        Children.Add(routerIcon);

        // Router label - centered properly
        var routerLabel = new TextBlock
        {
            Text = "Main Router",
            FontSize = 12,    
            FontWeight = FontWeights.Bold,  
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        // Measure the text to center it properly
        routerLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = routerLabel.DesiredSize.Width;
        
        Canvas.SetLeft(routerLabel, centerX - textWidth / 2);
        Canvas.SetTop(routerLabel, centerY + routerRadius + 10);
        Children.Add(routerLabel);

        // Simplified pulsing animation - only for very small networks
        if (Subnets!.Count <= 8)
        {
            var pulseAnimation = new DoubleAnimation
            {
                From = 0.9,
                To = 1.1,
                Duration = TimeSpan.FromSeconds(3), // Slower animation
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var scaleTransform = new ScaleTransform(1, 1, routerRadius, routerRadius);
            routerBg.RenderTransform = scaleTransform;
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
        }
    }

    private Line DrawConnectionLine(double x1, double y1, double x2, double y2, int index)
    {
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
            };
            line.BeginAnimation(OpacityProperty, fadeAnimation);
        }
        
        return line;
    }

    private Border DrawSubnetNode(SubnetInfo subnet, double x, double y, Brush color, int index)
    {
        // Create subnet container with optimized design
        var container = new Border
        {
            Width = 160,
            Height = 100,
            Background = GetCachedBrush(Color.FromArgb(200, 31, 41, 55)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = color,
            BorderThickness = new Thickness(2),
            // Simplified drop shadow - only for small networks
            Effect = Subnets!.Count <= 8 ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 6, // Reduced from 10
                Opacity = 0.2, // Reduced from 0.3
                ShadowDepth = 3 // Reduced from 5
            } : null
        };

        // Content stack
        var contentStack = new StackPanel
        {
            Margin = new Thickness(12, 8, 12, 8)
        };

        // Subnet header with icon
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 20,
            Height = 20,
            Background = color,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        
        var iconText = new TextBlock
        {
            Text = "🔌",
            FontSize = 10,
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
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(networkText, 1);
        headerGrid.Children.Add(networkText);

        contentStack.Children.Add(headerGrid);

        // Hosts info
        var hostsText = new TextBlock
        {
            Text = $"{subnet.UsableHosts} hosts",
            FontSize = 10,
            Foreground = GetCachedBrush(Color.FromRgb(156, 163, 175)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        contentStack.Children.Add(hostsText);

        // Required hosts
        var requiredText = new TextBlock
        {
            Text = $"Required: {subnet.RequiredHosts}",
            FontSize = 9,
            Foreground = GetCachedBrush(Color.FromRgb(245, 158, 11)),
            Margin = new Thickness(0, 1, 0, 0)
        };
        contentStack.Children.Add(requiredText);

        // Host range
        var rangeText = new TextBlock
        {
            Text = $"{subnet.FirstHost} - {subnet.LastHost}",
            FontSize = 9,
            Foreground = GetCachedBrush(Color.FromRgb(125, 211, 252)),
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        contentStack.Children.Add(rangeText);

        container.Child = contentStack;

        Canvas.SetLeft(container, x - 80);
        Canvas.SetTop(container, y - 50);

        // Simplified entrance animation - only for small networks
        if (Subnets!.Count <= 8)
        {
            var transformGroup = new TransformGroup();
            var scaleTransform = new ScaleTransform(0.8, 0.8, 80, 50);
            transformGroup.Children.Add(scaleTransform);
            
            container.RenderTransform = transformGroup;
            container.Opacity = 0;

            // Combined scale and fade animation
            var scaleAnimation = new DoubleAnimation
            {
                From = 0.8,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                BeginTime = TimeSpan.FromMilliseconds(100 + index * 50), // Reduced stagger
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
        }

        // Add interactive functionality
        AddInteractiveEvents(container);

        return container;
    }

    private void DrawBaseNetworkLabel()
    {
        var label = new Border
        {
            Background = GetCachedBrush(Color.FromArgb(180, 99, 102, 241)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8,
                Opacity = 0.3,
                ShadowDepth = 3
            }
        };

        var labelText = new TextBlock
        {
            Text = $"Base Network: {BaseNetwork}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas")
        };

        label.Child = labelText;

        Canvas.SetLeft(label, 20);
        Canvas.SetTop(label, 20);
        Children.Add(label);

        // Add fade-in animation
        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(500),
            BeginTime = TimeSpan.FromMilliseconds(100)
        };

        label.BeginAnimation(OpacityProperty, fadeAnimation);
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
                _animatingNodes.Remove(nodeInfo.Container);
            }
        };
        
        lock (_animationLock)
        {
            _activeAnimations[nodeInfo.Container] = storyboard;
            _animatingNodes.Add(nodeInfo.Container);
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
        CleanupAllAnimations();
        _currentDrawCancellation?.Cancel();
        _currentDrawCancellation?.Dispose();
        _currentDrawCancellation = null;
        
        // Clear caches
        _cachedBrushes.Clear();
    }
}
