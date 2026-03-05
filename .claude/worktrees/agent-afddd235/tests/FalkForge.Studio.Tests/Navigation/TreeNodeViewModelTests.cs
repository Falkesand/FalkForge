using FalkForge.Studio.Navigation;
using Xunit;

namespace FalkForge.Studio.Tests.Navigation;

public class TreeNodeViewModelTests
{
    [Fact]
    public void Constructor_SetsLabelAndKey()
    {
        var node = new TreeNodeViewModel("Product", "product");
        Assert.Equal("Product", node.Label);
        Assert.Equal("product", node.NodeKey);
    }

    [Fact]
    public void IsSelected_RaisesPropertyChanged()
    {
        var node = new TreeNodeViewModel("Test", "test");
        var raised = false;
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TreeNodeViewModel.IsSelected))
                raised = true;
        };
        node.IsSelected = true;
        Assert.True(raised);
        Assert.True(node.IsSelected);
    }

    [Fact]
    public void Children_CanAddNodes()
    {
        var parent = new TreeNodeViewModel("Root", "root");
        parent.Children.Add(new TreeNodeViewModel("Child", "child"));
        Assert.Single(parent.Children);
    }
}
