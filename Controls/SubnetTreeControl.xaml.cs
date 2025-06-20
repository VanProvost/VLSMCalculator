using System.Windows;
using System.Windows.Controls;
using VLSMCalculator.Models;

namespace VLSMCalculator.Controls;

public partial class SubnetTreeControl : UserControl
{
    private SubnetTree? _subnetTree;

    public SubnetTreeControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Find the SubnetTree control in the visual tree
        _subnetTree = this.FindName("SubnetTreeCanvas") as SubnetTree;
    }

    public void UpdateSubnetTree(string baseNetwork, List<SubnetInfo> subnets)
    {
        if (_subnetTree != null)
        {
            _subnetTree.BaseNetwork = baseNetwork;
            _subnetTree.Subnets = subnets;
        }
    }    public void ClearSubnetTree()
    {
        if (_subnetTree != null)
        {
            _subnetTree.BaseNetwork = string.Empty;
            _subnetTree.Subnets = new List<SubnetInfo>();
        }
    }
}
