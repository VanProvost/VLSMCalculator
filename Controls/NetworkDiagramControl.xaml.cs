using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VLSMCalculator.Models;

namespace VLSMCalculator.Controls;

public partial class NetworkDiagramControl : UserControl
{
    public static readonly DependencyProperty SubnetsProperty =
        DependencyProperty.Register(nameof(Subnets), typeof(ObservableCollection<SubnetInfo>), 
            typeof(NetworkDiagramControl), new PropertyMetadata(null, OnSubnetsChanged));

    public static readonly DependencyProperty BaseNetworkProperty =
        DependencyProperty.Register(nameof(BaseNetwork), typeof(string), 
            typeof(NetworkDiagramControl), new PropertyMetadata("", OnBaseNetworkChanged));

    public ObservableCollection<SubnetInfo>? Subnets
    {
        get => (ObservableCollection<SubnetInfo>?)GetValue(SubnetsProperty);
        set => SetValue(SubnetsProperty, value);
    }    public string BaseNetwork
    {
        get => (string)GetValue(BaseNetworkProperty);
        set => SetValue(BaseNetworkProperty, value);
    }public NetworkDiagramControl()
    {
        InitializeComponent();
    }    private static void OnSubnetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NetworkDiagramControl control)
        {
            var canvas = control.FindName("NetworkDiagramCanvas") as NetworkDiagram;
            if (canvas != null)
            {
                canvas.Subnets = e.NewValue as ObservableCollection<SubnetInfo>;
            }
        }
    }

    private static void OnBaseNetworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NetworkDiagramControl control)
        {
            var canvas = control.FindName("NetworkDiagramCanvas") as NetworkDiagram;
            if (canvas != null)
            {
                canvas.BaseNetwork = e.NewValue as string ?? "";
            }
        }
    }
}
