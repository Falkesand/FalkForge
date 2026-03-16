using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FalkForge.Studio.Graph;

namespace FalkForge.Studio.Editors.DependencyGraph;

public partial class DependencyGraphView : UserControl
{
    public DependencyGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DependencyGraphViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is DependencyGraphViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            RenderGraph(newVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DependencyGraphViewModel.StatusText) && sender is DependencyGraphViewModel vm)
            RenderGraph(vm);
    }

    private void RenderGraph(DependencyGraphViewModel vm)
    {
        GraphCanvas.Children.Clear();

        const double nodeWidth = 120;
        const double nodeHeight = 30;

        // Draw edges first (behind nodes)
        foreach (var edge in vm.GraphEdges)
        {
            var line = new Line
            {
                X1 = edge.X1,
                Y1 = edge.Y1,
                X2 = edge.X2,
                Y2 = edge.Y2,
                Stroke = Brushes.Gray,
                StrokeThickness = 1.5,
                StrokeDashArray = edge.EdgeType == "references" ? [4, 2] : []
            };
            GraphCanvas.Children.Add(line);
        }

        // Draw nodes
        foreach (var node in vm.GraphNodes)
        {
            var background = GetNodeBrush(node.NodeType, node.IsOrphaned);

            var border = new Border
            {
                Width = nodeWidth,
                Height = nodeHeight,
                Background = background,
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = node
            };

            var textBlock = new TextBlock
            {
                Text = node.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(4, 0, 4, 0)
            };

            border.Child = textBlock;
            border.MouseLeftButtonDown += OnNodeClick;
            border.ToolTip = $"{node.NodeType}: {node.Label}";

            Canvas.SetLeft(border, node.X);
            Canvas.SetTop(border, node.Y);
            GraphCanvas.Children.Add(border);
        }

        // Size the canvas to fit content
        double maxX = 0, maxY = 0;
        foreach (var node in vm.GraphNodes)
        {
            var right = node.X + nodeWidth + 20;
            var bottom = node.Y + nodeHeight + 20;
            if (right > maxX) maxX = right;
            if (bottom > maxY) maxY = bottom;
        }
        GraphCanvas.Width = Math.Max(800, maxX);
        GraphCanvas.Height = Math.Max(400, maxY);
    }

    private void OnNodeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: GraphNodeViewModel node } &&
            DataContext is DependencyGraphViewModel vm)
        {
            vm.OnNodeClicked(node);
        }
    }

    private static Brush GetNodeBrush(DependencyNodeType nodeType, bool isOrphaned)
    {
        if (isOrphaned)
            return Brushes.OrangeRed;

        return nodeType switch
        {
            DependencyNodeType.Feature => new SolidColorBrush(Color.FromRgb(66, 133, 244)),   // Blue
            DependencyNodeType.File => new SolidColorBrush(Color.FromRgb(52, 168, 83)),       // Green
            DependencyNodeType.Service => new SolidColorBrush(Color.FromRgb(234, 134, 0)),    // Orange
            DependencyNodeType.Registry => new SolidColorBrush(Color.FromRgb(142, 68, 173)),  // Purple
            DependencyNodeType.Shortcut => new SolidColorBrush(Color.FromRgb(0, 172, 193)),   // Teal
            _ => Brushes.Gray
        };
    }
}
