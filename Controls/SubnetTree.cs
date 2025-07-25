using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VLSMCalculator.Models;
using VLSMCalculator.Services;

namespace VLSMCalculator.Controls;

public class SubnetTree : Canvas
{
    public static readonly DependencyProperty BaseNetworkProperty =
        DependencyProperty.Register(nameof(BaseNetwork), typeof(string), typeof(SubnetTree),
            new PropertyMetadata(string.Empty, OnBaseNetworkChanged));

    public static readonly DependencyProperty SubnetsProperty =
        DependencyProperty.Register(nameof(Subnets), typeof(List<SubnetInfo>), typeof(SubnetTree),
            new PropertyMetadata(null, OnSubnetsChanged));    private const double NodeWidth = 200;
    private const double NodeHeight = 80;
    private const double LevelSpacing = 120;
    private const double NodeSpacing = 40;
    private readonly SolidColorBrush UsedBrush = new(Color.FromRgb(76, 175, 80));           // Green - allocated subnets
    private readonly SolidColorBrush SubdividedBrush = new(Color.FromRgb(255, 152, 0));     // Orange - subdivided subnets
    private readonly SolidColorBrush AvailableBrush = new(Color.FromRgb(158, 158, 158));    // Gray - available/root
    private readonly SolidColorBrush LineBrush = new(Color.FromRgb(100, 100, 100));
    private readonly SolidColorBrush TextBrush = new(Colors.White);

    public string BaseNetwork
    {
        get => (string)GetValue(BaseNetworkProperty);
        set => SetValue(BaseNetworkProperty, value);
    }

    public List<SubnetInfo> Subnets
    {
        get => (List<SubnetInfo>)GetValue(SubnetsProperty);
        set => SetValue(SubnetsProperty, value);
    }

    private static void OnBaseNetworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SubnetTree tree)
            tree.DrawTree();
    }

    private static void OnSubnetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SubnetTree tree)
            tree.DrawTree();
    }

    private void DrawTree()
    {
        Children.Clear();

        if (string.IsNullOrEmpty(BaseNetwork) || Subnets == null || !Subnets.Any())
        {
            DrawEmptyState();
            return;
        }

        try
        {
            var parts = BaseNetwork.Split('/');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var baseCidr))
                return;

            var baseNetworkAddress = parts[0];
            var baseNetInt = NetworkCalculationService.ConvertIPToUInt32(baseNetworkAddress);

            // Create tree structure
            var treeNodes = CreateTreeStructure(baseNetInt, baseCidr);

            // Draw the tree
            DrawTreeNodes(treeNodes);
        }
        catch (Exception ex)
        {
            DrawErrorState($"Error creating subnet tree: {ex.Message}");
        }
    }    private List<TreeNode> CreateTreeStructure(uint baseNetInt, int baseCidr)
    {
        var nodes = new List<TreeNode>();
        
        // Create root node
        var rootNode = new TreeNode
        {
            NetworkInt = baseNetInt,
            CIDR = baseCidr,
            NetworkAddress = NetworkCalculationService.ConvertUInt32ToIP(baseNetInt),
            Level = 0,
            IsUsed = false,
            IsAllocated = false,
            Children = new List<TreeNode>()
        };
        
        nodes.Add(rootNode);

        if (Subnets == null || !Subnets.Any())
            return nodes;

        // Build VLSM pyramid showing actual subnet allocation process
        BuildVLSMPyramid(rootNode, nodes, baseCidr);

        return nodes;
    }    private void BuildVLSMPyramid(TreeNode rootNode, List<TreeNode> allNodes, int baseCidr)
    {
        // Sort subnets by size (largest first) - this is how VLSM allocation works
        var sortedSubnets = Subnets.OrderByDescending(s => s.UsableHosts).ToList();
        
        // Create comprehensive tree structure showing ALL intermediary steps
        var allIntermediarySteps = new HashSet<(uint NetworkInt, int CIDR)>();
        
        // For each subnet, add ALL intermediary steps from base CIDR to target CIDR
        foreach (var subnet in sortedSubnets)
        {
            var subnetNetInt = NetworkCalculationService.ConvertIPToUInt32(subnet.NetworkAddress);
            
            // Add all intermediary steps from base CIDR to target CIDR
            for (int cidr = baseCidr + 1; cidr <= subnet.CIDR; cidr++)
            {
                var networkMask = 0xFFFFFFFFU << (32 - cidr);
                var networkInt = subnetNetInt & networkMask;
                allIntermediarySteps.Add((networkInt, cidr));
            }
        }

        // Convert to subdivision steps
        var subdivisionSteps = new List<SubdivisionStep>();
        
        foreach (var (networkInt, cidr) in allIntermediarySteps)
        {
            var networkAddress = NetworkCalculationService.ConvertUInt32ToIP(networkInt);
            var matchingSubnet = sortedSubnets.FirstOrDefault(s => 
                s.CIDR == cidr && 
                NetworkCalculationService.ConvertIPToUInt32(s.NetworkAddress) == networkInt);
            
            var step = new SubdivisionStep
            {
                NetworkInt = networkInt,
                CIDR = cidr,
                NetworkAddress = networkAddress,
                IsAllocated = matchingSubnet != null,
                IsIntermediate = matchingSubnet == null,
                TargetSubnet = matchingSubnet,
                SourceCIDR = baseCidr,
                Level = cidr - baseCidr
            };
            
            subdivisionSteps.Add(step);
        }

        // Build tree from subdivision steps
        BuildTreeFromSubdivisions(rootNode, allNodes, subdivisionSteps);
    }private void BuildTreeFromSubdivisions(TreeNode rootNode, List<TreeNode> allNodes, List<SubdivisionStep> subdivisionSteps)
    {
        // Sort steps by level and then by network address to ensure proper hierarchy
        var sortedSteps = subdivisionSteps.OrderBy(s => s.Level).ThenBy(s => s.NetworkInt).ToList();
        
        foreach (var step in sortedSteps)
        {
            // Find the best parent node (most specific parent that contains this network)
            var parentNode = FindBestParentNode(allNodes, step) ?? rootNode;

            // Create node for this subdivision step
            var stepNode = new TreeNode
            {
                NetworkInt = step.NetworkInt,
                CIDR = step.CIDR,
                NetworkAddress = step.NetworkAddress,
                Level = step.Level, // Use the level from the step directly
                IsUsed = step.IsAllocated,
                IsAllocated = step.IsAllocated,
                IsIntermediate = step.IsIntermediate,
                Parent = parentNode,
                SubnetInfo = step.TargetSubnet,
                Children = new List<TreeNode>()
            };

            // Avoid duplicate nodes by checking both network and CIDR
            if (!allNodes.Any(n => n.NetworkInt == stepNode.NetworkInt && n.CIDR == stepNode.CIDR))
            {
                parentNode.Children.Add(stepNode);
                allNodes.Add(stepNode);
            }
        }
    }    private TreeNode? FindBestParentNode(List<TreeNode> allNodes, SubdivisionStep step)
    {
        // Find the most specific node that contains this step
        // The parent should have a smaller CIDR (less specific) and should contain this network
        return allNodes
            .Where(n => n.CIDR < step.CIDR &&
                       IsNetworkContained(step.NetworkInt, step.CIDR, n.NetworkInt, n.CIDR))
            .OrderByDescending(n => n.CIDR)  // Most specific (largest CIDR) first
            .FirstOrDefault();
    }    private bool IsNetworkContained(uint childNetInt, int childCIDR, uint parentNetInt, int parentCIDR)
    {
        if (parentCIDR >= childCIDR) return false;
        
        var parentMask = 0xFFFFFFFFU << (32 - parentCIDR);
        var parentNetwork = parentNetInt & parentMask;
        var childNetwork = childNetInt & parentMask;
        
        return parentNetwork == childNetwork;
    }

    private void DrawTreeNodes(List<TreeNode> nodes)
    {
        var nodePositions = CalculateNodePositions(nodes);
          // Draw connections first (so they appear behind nodes)
        foreach (var node in nodes.Where(n => n.Parent != null))
        {
            if (node.Parent != null && nodePositions.ContainsKey(node.Parent) && nodePositions.ContainsKey(node))
            {
                DrawConnection(nodePositions[node.Parent], nodePositions[node]);
            }
        }

        // Draw nodes
        foreach (var node in nodes)
        {
            DrawNode(node, nodePositions[node]);
        }

        // Update canvas size based on actual node positions
        if (nodePositions.Any())
        {
            var maxX = nodePositions.Values.Max(p => p.X) + NodeWidth + 20;
            var maxY = nodePositions.Values.Max(p => p.Y) + NodeHeight + 20;
            Width = Math.Max(600, maxX);
            Height = Math.Max(400, maxY);
        }
        else
        {
            Width = 600;
            Height = 400;
        }
    }private Dictionary<TreeNode, Point> CalculateNodePositions(List<TreeNode> nodes)
    {
        var positions = new Dictionary<TreeNode, Point>();
        var levelCounts = nodes.GroupBy(n => n.Level).ToDictionary(g => g.Key, g => g.Count());

        // Calculate the maximum width needed for any level
        var maxNodesInLevel = levelCounts.Values.Max();
        var maxLevelWidth = (maxNodesInLevel - 1) * (NodeWidth + NodeSpacing) + NodeWidth;
        var canvasWidth = Math.Max(600, maxLevelWidth + 40); // Add padding

        // Calculate positions level by level
        foreach (var level in levelCounts.Keys.OrderBy(x => x))
        {
            var nodesAtLevel = nodes.Where(n => n.Level == level).OrderBy(n => n.NetworkInt).ToList();
            var levelWidth = (nodesAtLevel.Count - 1) * (NodeWidth + NodeSpacing) + NodeWidth;
            var startX = Math.Max(20, (canvasWidth - levelWidth) / 2);

            for (int i = 0; i < nodesAtLevel.Count; i++)
            {
                var node = nodesAtLevel[i];
                var x = startX + i * (NodeWidth + NodeSpacing);
                var y = 20 + level * LevelSpacing;
                positions[node] = new Point(x, y);
            }
        }

        return positions;
    }

    private void DrawConnection(Point parentPos, Point childPos)
    {
        var line = new Line
        {
            X1 = parentPos.X + NodeWidth / 2,
            Y1 = parentPos.Y + NodeHeight,
            X2 = childPos.X + NodeWidth / 2,
            Y2 = childPos.Y,
            Stroke = LineBrush,
            StrokeThickness = 2
        };

        Children.Add(line);
    }    private void DrawNode(TreeNode node, Point position)
    {
        // Determine node color based on its role in the VLSM pyramid
        SolidColorBrush backgroundColor;
        string nodeTypeLabel;
        
        if (node.IsUsed && node.SubnetInfo != null)
        {
            // Final allocated subnet - green
            backgroundColor = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            nodeTypeLabel = "ALLOCATED";
        }
        else if (node.IsIntermediate)
        {
            // Intermediate subdivision step - orange
            backgroundColor = new SolidColorBrush(Color.FromRgb(255, 152, 0));
            nodeTypeLabel = "SUBDIVISION";
        }
        else if (node.Level == 0)
        {
            // Root network - blue
            backgroundColor = new SolidColorBrush(Color.FromRgb(63, 81, 181));
            nodeTypeLabel = "ROOT";
        }
        else
        {
            // Available/unused space - gray
            backgroundColor = new SolidColorBrush(Color.FromRgb(158, 158, 158));
            nodeTypeLabel = "AVAILABLE";
        }

        // Create node border with slightly larger size for better visibility
        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Background = backgroundColor,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(2)
        };

        // Create content stack panel
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };        // Add node type badge
        var typeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var typeText = new TextBlock
        {
            Text = nodeTypeLabel,
            Foreground = TextBrush,
            FontSize = 7,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        typeBadge.Child = typeText;
        content.Children.Add(typeBadge);

        // Main CIDR notation - prominently displayed
        var cidrText = new TextBlock
        {
            Text = $"/{node.CIDR}",
            Foreground = TextBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        content.Children.Add(cidrText);

        // Show network address
        var addressText = new TextBlock
        {
            Text = node.NetworkAddress,
            Foreground = TextBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        content.Children.Add(addressText);

        // Additional info based on node type
        if (node.IsUsed && node.SubnetInfo != null)
        {
            var hostsText = new TextBlock
            {
                Text = $"{node.SubnetInfo.RequiredHosts} hosts needed",
                Foreground = TextBrush,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            content.Children.Add(hostsText);

            var usableText = new TextBlock
            {
                Text = $"{node.SubnetInfo.UsableHosts} usable",
                Foreground = TextBrush,
                FontSize = 7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
                FontStyle = FontStyles.Italic
            };
            content.Children.Add(usableText);
        }
        else if (node.IsIntermediate)
        {
            var splitText = new TextBlock
            {
                Text = "Split Point",
                Foreground = TextBrush,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
                FontStyle = FontStyles.Italic
            };
            content.Children.Add(splitText);
        }
        else if (node.Level == 0)
        {
            var baseText = new TextBlock
            {
                Text = "Base Network",
                Foreground = TextBrush,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
                FontStyle = FontStyles.Italic
            };
            content.Children.Add(baseText);
        }

        border.Child = content;

        // Position the border
        SetLeft(border, position.X);
        SetTop(border, position.Y);

        Children.Add(border);
    }private void DrawEmptyState()
    {
        // Ensure canvas has proper dimensions
        Width = Math.Max(600, Width);
        Height = Math.Max(400, Height);
        
        var placeholder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Width = 400,
            Height = 200
        };

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new TextBlock
        {
            Text = "🌳",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 15)
        };

        var message = new TextBlock
        {
            Text = "Enter network information to view subnet tree",
            FontSize = 16,
            FontWeight = FontWeights.Medium,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        content.Children.Add(icon);
        content.Children.Add(message);
        placeholder.Child = content;

        var centerX = Math.Max(0, (Width - 400) / 2);
        var centerY = Math.Max(0, (Height - 200) / 2);
        SetLeft(placeholder, centerX);
        SetTop(placeholder, centerY);

        Children.Add(placeholder);
    }

    private void DrawErrorState(string error)
    {
        // Ensure canvas has proper dimensions
        Width = Math.Max(600, Width);
        Height = Math.Max(400, Height);
        
        var errorBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(80, 20, 20)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Width = 400,
            Height = 150
        };

        var content = new StackPanel();

        var icon = new TextBlock
        {
            Text = "⚠️",
            FontSize = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var errorText = new TextBlock
        {
            Text = error,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.LightCoral),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        content.Children.Add(icon);
        content.Children.Add(errorText);
        errorBorder.Child = content;

        var centerX = Math.Max(0, (Width - 400) / 2);
        var centerY = Math.Max(0, (Height - 150) / 2);
        SetLeft(errorBorder, centerX);
        SetTop(errorBorder, centerY);        Children.Add(errorBorder);
    }

    private class TreeNode
    {
        public uint NetworkInt { get; set; }
        public int CIDR { get; set; }
        public string NetworkAddress { get; set; } = string.Empty;
        public int Level { get; set; }
        public bool IsUsed { get; set; }
        public bool IsAllocated { get; set; }
        public bool IsIntermediate { get; set; }  // New property for intermediate subdivision nodes
        public bool IsLeft { get; set; }
        public TreeNode? Parent { get; set; }
        public List<TreeNode> Children { get; set; } = new();
        public SubnetInfo? SubnetInfo { get; set; }
    }

    private class SubdivisionStep
    {
        public uint NetworkInt { get; set; }
        public int CIDR { get; set; }
        public string NetworkAddress { get; set; } = string.Empty;
        public bool IsAllocated { get; set; }
        public bool IsIntermediate { get; set; }
        public SubnetInfo? TargetSubnet { get; set; }
        public int SourceCIDR { get; set; }
        public int Level { get; set; }
    }
}
