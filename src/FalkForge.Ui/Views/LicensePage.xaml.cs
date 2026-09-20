using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FalkForge.Ui.ViewModels;

namespace FalkForge.Ui.Views;

public partial class LicensePage : UserControl
{
    public LicensePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => DisplayLicense();
    }

    private void DisplayLicense()
    {
        LicenseViewer.Document = new FlowDocument();
        if (DataContext is not LicensePageViewModel viewModel)
            return;

        if (viewModel.LicenseContent is { } content && content.AsSpan().StartsWith("{\\rtf"u8))
        {
            try
            {
                using var stream = new MemoryStream(content, writable: false);
                var document = LicenseViewer.Document;
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                range.Load(stream, DataFormats.Rtf);
                if (!string.IsNullOrWhiteSpace(range.Text))
                    return;
            }
            catch (ArgumentException)
            {
                // WPF rejected the RTF. Do not let the user accept an unreadable agreement.
            }

            viewModel.RejectUnreadableLicense();
            LicenseViewer.Document = new FlowDocument(new Paragraph(
                new Run("The license agreement could not be displayed. Please contact the publisher.")));
            return;
        }

        LicenseViewer.Document.Blocks.Add(new Paragraph(new Run(viewModel.LicenseText)));
    }
}