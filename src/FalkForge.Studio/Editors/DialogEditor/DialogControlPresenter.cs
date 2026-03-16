using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.DialogEditor;

public sealed class DialogControlPresenter : Border
{
    public static readonly DependencyProperty ControlProperty =
        DependencyProperty.Register(
            nameof(Control),
            typeof(DialogControlDefinition),
            typeof(DialogControlPresenter),
            new PropertyMetadata(null, OnControlChanged));

    public DialogControlDefinition? Control
    {
        get => (DialogControlDefinition?)GetValue(ControlProperty);
        set => SetValue(ControlProperty, value);
    }

    private static void OnControlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DialogControlPresenter presenter)
            presenter.BuildVisual();
    }

    private void BuildVisual()
    {
        Child = null;
        if (Control is null) return;

        Child = Control.Type switch
        {
            DialogControlType.PushButton => BuildButton(),
            DialogControlType.TextEdit => BuildTextBox(),
            DialogControlType.PathEdit => BuildTextBox(),
            DialogControlType.CheckBox => BuildCheckBox(),
            DialogControlType.ComboBox => BuildComboBox(),
            DialogControlType.ListBox => BuildListBox(),
            DialogControlType.Line => BuildLine(),
            DialogControlType.ProgressBar => BuildProgressBar(),
            DialogControlType.Bitmap => BuildBitmap(),
            DialogControlType.RadioButtonGroup => BuildRadioGroup(),
            DialogControlType.DirectoryCombo => BuildComboBox(),
            DialogControlType.VolumeCostList => BuildListBox(),
            _ => BuildTextBlock()
        };
    }

    private UIElement BuildTextBlock()
    {
        return new TextBlock
        {
            Text = Control?.Text ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10
        };
    }

    private UIElement BuildButton()
    {
        return new Button
        {
            Content = Control?.Text ?? "Button",
            FontSize = 10,
            IsHitTestVisible = false
        };
    }

    private UIElement BuildTextBox()
    {
        return new TextBox
        {
            Text = Control?.Text ?? string.Empty,
            FontSize = 10,
            IsReadOnly = true,
            IsHitTestVisible = false
        };
    }

    private UIElement BuildCheckBox()
    {
        return new CheckBox
        {
            Content = Control?.Text ?? "CheckBox",
            FontSize = 10,
            IsHitTestVisible = false
        };
    }

    private UIElement BuildComboBox()
    {
        return new ComboBox
        {
            FontSize = 10,
            IsHitTestVisible = false
        };
    }

    private UIElement BuildListBox()
    {
        return new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White
        };
    }

    private UIElement BuildLine()
    {
        return new Rectangle
        {
            Height = 1,
            Fill = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private UIElement BuildProgressBar()
    {
        return new ProgressBar
        {
            Value = 45,
            Minimum = 0,
            Maximum = 100,
            IsHitTestVisible = false
        };
    }

    private UIElement BuildBitmap()
    {
        return new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.LightYellow,
            Child = new TextBlock
            {
                Text = "[Bitmap]",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private UIElement BuildRadioGroup()
    {
        var panel = new StackPanel();
        panel.Children.Add(new RadioButton { Content = "Option 1", FontSize = 10, IsHitTestVisible = false });
        panel.Children.Add(new RadioButton { Content = "Option 2", FontSize = 10, IsHitTestVisible = false });
        return panel;
    }
}
