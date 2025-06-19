using System.Windows;
using System;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using VLSMCalculator.Services;
using VLSMCalculator.Models;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;

namespace VLSMCalculator.Views;

public partial class MainWindow : Window
{
    private ObservableCollection<SubnetInfo> _subnets = new();
    private DispatcherTimer _updateTimer;
    private CancellationTokenSource? _currentCalculationCTS;
    private int _calculationSequence = 0;public MainWindow()
    {
        InitializeComponent();
        
        // Initialize timer for delayed auto-update
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800) // Wait 800ms after user stops typing
        };
        _updateTimer.Tick += UpdateTimer_Tick;
          // Set initial state after the window is loaded
        this.Loaded += MainWindow_Loaded;
        
        // Clean up resources when window is closing
        this.Closing += MainWindow_Closing;
    }    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateUI();
    }    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Cancel any ongoing calculations
        CancelCurrentCalculation();
        
        // Stop and dispose timer
        _updateTimer?.Stop();
        _updateTimer = null!;
    }

    private void CalculateButton_Click(object sender, RoutedEventArgs e)
    {
        PerformCalculation();
    }    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        // Cancel any ongoing calculations
        CancelCurrentCalculation();
        
        var networkInput = FindName("NetworkInput") as TextBox;
        var hostRequirementsInput = FindName("HostRequirementsInput") as TextBox;
        var networkDiagram = FindName("NetworkDiagramCanvas") as VLSMCalculator.Controls.NetworkDiagram;
        
        if (networkInput != null) networkInput.Text = "192.168.1.0/24";
        if (hostRequirementsInput != null) hostRequirementsInput.Text = "50,25,10,5";
        HideError();
        
        // Clear diagram completely
        _subnets.Clear();
        if (networkDiagram != null)
        {
            networkDiagram.Subnets = null;
            networkDiagram.BaseNetwork = "";
            
            // Use Dispatcher to ensure UI updates happen properly
            Dispatcher.BeginInvoke(() =>
            {
                networkDiagram.Subnets = new ObservableCollection<SubnetInfo>();
            }, DispatcherPriority.Render);
        }
        
        // Show placeholder
        ShowPlaceholder();
    }private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        _updateTimer.Stop();
        var autoUpdateCheckBox = FindName("AutoUpdateCheckBox") as CheckBox;
        if (autoUpdateCheckBox?.IsChecked == true)
        {
            PerformCalculation();
        }
    }

    private void NetworkInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var autoUpdateCheckBox = FindName("AutoUpdateCheckBox") as CheckBox;
        if (autoUpdateCheckBox?.IsChecked == true)
        {
            RestartUpdateTimer();
        }
    }

    private void HostRequirementsInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var autoUpdateCheckBox = FindName("AutoUpdateCheckBox") as CheckBox;
        if (autoUpdateCheckBox?.IsChecked == true)
        {
            RestartUpdateTimer();
        }
    }

    private void AutoUpdateCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var autoUpdateCheckBox = FindName("AutoUpdateCheckBox") as CheckBox;
        if (autoUpdateCheckBox?.IsChecked == true)
        {
            RestartUpdateTimer();
        }
    }    private void RestartUpdateTimer()
    {
        if (_updateTimer != null)
        {
            _updateTimer.Stop();
            _updateTimer.Start();
            
            // Also cancel any ongoing calculation since user is still typing
            CancelCurrentCalculation();
        }
    }

    private void UpdateUI()
    {
        // Initial UI state
        ShowPlaceholder();
    }    private void ShowPlaceholder()
    {
        var placeholderPanel = FindName("PlaceholderPanel") as Border;
        var subnetCountBadge = FindName("SubnetCountBadge") as Border;
        
        if (placeholderPanel != null) placeholderPanel.Visibility = Visibility.Visible;
        if (subnetCountBadge != null) subnetCountBadge.Visibility = Visibility.Collapsed;
        HideError();
    }

    private void ShowError(string message)
    {
        var errorMessage = FindName("ErrorMessage") as TextBlock;
        var errorPanel = FindName("ErrorPanel") as Border;
        
        if (errorMessage != null) errorMessage.Text = message;
        if (errorPanel != null) errorPanel.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        var errorMessage = FindName("ErrorMessage") as TextBlock;
        var errorPanel = FindName("ErrorPanel") as Border;
        
        if (errorMessage != null) errorMessage.Text = "";
        if (errorPanel != null) errorPanel.Visibility = Visibility.Collapsed;
    }    private void PerformCalculation()
    {
        // Cancel any existing calculation
        CancelCurrentCalculation();
        
        // Increment sequence number for this calculation
        var currentSequence = Interlocked.Increment(ref _calculationSequence);
        
        // Create new cancellation token for this calculation
        _currentCalculationCTS = new CancellationTokenSource();
        var cancellationToken = _currentCalculationCTS.Token;
        
        // Perform calculation asynchronously to avoid blocking UI
        _ = Task.Run(async () =>
        {
            try
            {
                await PerformCalculationAsync(currentSequence, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Calculation was cancelled, this is expected
                Console.WriteLine($"Calculation {currentSequence} was cancelled");
            }
            catch (Exception ex)
            {                // Handle other exceptions on UI thread
                await Dispatcher.BeginInvoke(() =>
                {
                    if (!cancellationToken.IsCancellationRequested && currentSequence == _calculationSequence)
                    {
                        ShowError($"Calculation error: {ex.Message}");
                        ShowPlaceholder();
                    }
                });
            }
        }, cancellationToken);
    }

    private void CancelCurrentCalculation()
    {
        try
        {
            _currentCalculationCTS?.Cancel();
            _currentCalculationCTS?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
        _currentCalculationCTS = null;
    }

    private async Task PerformCalculationAsync(int sequenceNumber, CancellationToken cancellationToken)
    {
        // Check if this calculation is still relevant
        if (sequenceNumber != _calculationSequence || cancellationToken.IsCancellationRequested)
            return;

        // Get input values on UI thread
        string networkInput = "";
        string hostReqText = "";
        
        await Dispatcher.BeginInvoke(() =>
        {
            var networkInputControl = FindName("NetworkInput") as TextBox;
            var hostRequirementsInputControl = FindName("HostRequirementsInput") as TextBox;
            networkInput = networkInputControl?.Text.Trim() ?? "";
            hostReqText = hostRequirementsInputControl?.Text.Trim() ?? "";
        });

        // Check cancellation again
        cancellationToken.ThrowIfCancellationRequested();

        // Validate input
        if (string.IsNullOrEmpty(networkInput) || string.IsNullOrEmpty(hostReqText))
        {
            await Dispatcher.BeginInvoke(() =>
            {
                if (sequenceNumber == _calculationSequence)
                    ShowPlaceholder();
            });
            return;
        }

        // Parse and validate network input
        if (!networkInput.Contains('/'))
        {
            await Dispatcher.BeginInvoke(() =>
            {
                if (sequenceNumber == _calculationSequence)
                    ShowError("Network must be in CIDR format (e.g., 192.168.1.0/24)");
            });
            return;
        }

        var parts = networkInput.Split('/');
        var baseNetwork = parts[0];
        int cidr;
        
        if (!int.TryParse(parts[1], out cidr))
        {
            await Dispatcher.BeginInvoke(() =>
            {
                if (sequenceNumber == _calculationSequence)
                    ShowError("Invalid CIDR notation");
            });
            return;
        }

        // Parse host requirements
        int[] hostRequirements;
        try
        {
            hostRequirements = hostReqText.Split(',')
                .Select(x => int.Parse(x.Trim()))
                .ToArray();
        }
        catch
        {
            await Dispatcher.BeginInvoke(() =>
            {
                if (sequenceNumber == _calculationSequence)
                    ShowError("Invalid host requirements format");
            });
            return;
        }

        // Check cancellation before heavy computation
        cancellationToken.ThrowIfCancellationRequested();

        // Perform VLSM calculation (this is the potentially heavy operation)
        List<SubnetInfo> subnets;
        try
        {
            // Add small delay to allow for rapid input cancellation
            await Task.Delay(50, cancellationToken);
            
            subnets = VLSMCalculationService.GetVLSMAllocation(baseNetwork, cidr, hostRequirements);
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation
        }
        catch (Exception ex)
        {
            await Dispatcher.BeginInvoke(() =>
            {
                if (sequenceNumber == _calculationSequence)
                {
                    ShowError(ex.Message);
                    ShowPlaceholder();
                }
            });
            return;
        }

        // Final cancellation check before updating UI
        cancellationToken.ThrowIfCancellationRequested();

        // Update UI on main thread
        await Dispatcher.BeginInvoke(() =>
        {
            // Only update if this is still the latest calculation
            if (sequenceNumber == _calculationSequence && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    HideError();
                    
                    // Create new ObservableCollection for diagram
                    var newSubnets = new ObservableCollection<SubnetInfo>(subnets);
                    _subnets = newSubnets;
                    
                    // Update network diagram
                    var networkDiagram = FindName("NetworkDiagramCanvas") as VLSMCalculator.Controls.NetworkDiagram;
                    if (networkDiagram != null)
                    {
                        networkDiagram.Subnets = null;
                        networkDiagram.BaseNetwork = "";
                        
                        // Use another dispatcher call to ensure proper refresh
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (sequenceNumber == _calculationSequence) // Check again
                            {
                                networkDiagram.Subnets = newSubnets;
                                networkDiagram.BaseNetwork = networkInput;
                            }
                        }, DispatcherPriority.Render);
                    }

                    // Update results UI
                    UpdateResultsUI(subnets, networkInput, cidr);
                    
                    // Update UI elements
                    var placeholderPanel = FindName("PlaceholderPanel") as Border;
                    var subnetCountBadge = FindName("SubnetCountBadge") as Border;
                    var subnetCountText = FindName("SubnetCountText") as TextBlock;
                    
                    if (placeholderPanel != null) placeholderPanel.Visibility = Visibility.Collapsed;
                    if (subnetCountBadge != null) subnetCountBadge.Visibility = Visibility.Visible;
                    if (subnetCountText != null) subnetCountText.Text = $"{subnets.Count} Subnet{(subnets.Count != 1 ? "s" : "")}";
                }
                catch (Exception ex)
                {
                    ShowError($"UI Update error: {ex.Message}");
                    ShowPlaceholder();
                }
            }
        });
    }private void UpdateResultsUI(List<SubnetInfo> subnets, string networkInput, int cidr)
    {
        var resultsPanel = FindName("ResultsPanel") as StackPanel;
        if (resultsPanel == null) return;
        
        resultsPanel.Children.Clear();
        
        // Add summary card
        var summaryCard = CreateSummaryCard(subnets, networkInput, cidr);
        resultsPanel.Children.Add(summaryCard);
        
        // Add subnet cards
        for (int i = 0; i < subnets.Count; i++)
        {
            var subnetCard = CreateSubnetCard(subnets[i], i + 1);
            resultsPanel.Children.Add(subnetCard);
        }
    }

    private Border CreateSummaryCard(List<SubnetInfo> subnets, string networkInput, int cidr)
    {
        var totalUsedHosts = subnets.Sum(s => s.TotalHosts);
        var totalAvailable = (int)Math.Pow(2, (32 - cidr));
        var efficiency = Math.Round((double)totalUsedHosts / totalAvailable * 100, 2);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 59, 130, 246)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 15)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftStack = new StackPanel();
        leftStack.Children.Add(new TextBlock 
        { 
            Text = "📊 Summary", 
            FontSize = 16, 
            FontWeight = FontWeights.Bold, 
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        });
        leftStack.Children.Add(new TextBlock 
        { 
            Text = $"Base Network: {networkInput}", 
            FontSize = 12, 
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas")
        });
        leftStack.Children.Add(new TextBlock 
        { 
            Text = $"Total Subnets: {subnets.Count}", 
            FontSize = 12, 
            Foreground = Brushes.White 
        });

        var rightStack = new StackPanel();
        rightStack.Children.Add(new TextBlock 
        { 
            Text = $"Efficiency: {efficiency}%", 
            FontSize = 14, 
            FontWeight = FontWeights.Bold, 
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        rightStack.Children.Add(new TextBlock 
        { 
            Text = $"{totalUsedHosts:N0} / {totalAvailable:N0} addresses", 
            FontSize = 12, 
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right
        });

        Grid.SetColumn(leftStack, 0);
        Grid.SetColumn(rightStack, 1);
        grid.Children.Add(leftStack);
        grid.Children.Add(rightStack);
        card.Child = grid;

        return card;
    }    private Border CreateSubnetCard(SubnetInfo subnet, int subnetNumber)
    {
        var card = new Border
        {
            Background = (Brush)Application.Current.Resources["SystemControlBackgroundChromeMediumBrush"],
            BorderBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumLowBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        // Subnet number badge
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 99, 102, 241)),
            CornerRadius = new CornerRadius(20),
            Width = 40,
            Height = 40,
            VerticalAlignment = VerticalAlignment.Top
        };
        badge.Child = new TextBlock
        {
            Text = subnetNumber.ToString(),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };        // Subnet details
        var detailsStack = new StackPanel { Margin = new Thickness(15, 0, 0, 0) };
        detailsStack.Children.Add(new TextBlock 
        { 
            Text = $"Subnet #{subnetNumber}", 
            FontSize = 14, 
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            Margin = new Thickness(0, 0, 0, 5)
        });
        detailsStack.Children.Add(new TextBlock 
        { 
            Text = $"Network: {subnet.Network}", 
            FontSize = 12, 
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            Margin = new Thickness(0, 2, 0, 2)
        });
        detailsStack.Children.Add(new TextBlock 
        { 
            Text = $"Range: {subnet.FirstHost} - {subnet.LastHost}", 
            FontSize = 12, 
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            Margin = new Thickness(0, 2, 0, 2)
        });
        detailsStack.Children.Add(new TextBlock 
        { 
            Text = $"Mask: {subnet.SubnetMask}", 
            FontSize = 12, 
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            Margin = new Thickness(0, 2, 0, 2)
        });        // Host count
        var hostCountStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        hostCountStack.Children.Add(new TextBlock 
        { 
            Text = "Hosts", 
            FontSize = 10, 
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            HorizontalAlignment = HorizontalAlignment.Center
        });
        hostCountStack.Children.Add(new TextBlock 
        { 
            Text = subnet.UsableHosts.ToString("N0"), 
            FontSize = 18, 
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            HorizontalAlignment = HorizontalAlignment.Center
        });
        hostCountStack.Children.Add(new TextBlock 
        { 
            Text = $"Required: {subnet.RequiredHosts:N0}", 
            FontSize = 10, 
            Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            HorizontalAlignment = HorizontalAlignment.Center
        });

        Grid.SetColumn(badge, 0);
        Grid.SetColumn(detailsStack, 1);
        Grid.SetColumn(hostCountStack, 2);
        
        mainGrid.Children.Add(badge);
        mainGrid.Children.Add(detailsStack);
        mainGrid.Children.Add(hostCountStack);
        
        card.Child = mainGrid;
        return card;
    }
}
