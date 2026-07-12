using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves that an extension-contributed <see cref="IMsiDialogStepBuilder"/> referenced via
/// <c>DialogCustomization.InsertStep</c> is now genuinely emitted into the MSI UI tables — not
/// merely name-resolved for DLG001. Previously the only path for such a step threw at emit time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAuthoringExtensionDialogEmissionTests : IDisposable
{
    private readonly string _tempDir;

    public MsiAuthoringExtensionDialogEmissionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiAuthoringExtDlg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class CompanyLicenseStepBuilder : IMsiDialogStepBuilder
    {
        public string Name => "CompanyLicenseDlg";

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var model = new MsiDialogModel { Name = Name, Title = "Company License", FirstControl = "Body" };
            model.Controls.Add(new MsiControlModel
            {
                Name = "Body", Type = MsiControlType.ScrollableText,
                X = 20, Y = 20, Width = 330, Height = 200, Text = "License text.",
            });
            return model;
        }
    }

    private sealed class CompanyLicenseExtension : IFalkForgeExtension
    {
        public string Name => "CompanyLicenseExtension";
        public string Version => "1.0.0";
        public string MinHostVersion => "0.0.0";

        public void Register(IExtensionRegistry registry)
            => registry.RegisterDialogStep(new CompanyLicenseStepBuilder());
    }

    [Fact]
    public void Inserted_extension_dialog_step_emits_its_dialog_and_control_rows()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.txt");
        File.WriteAllText(sourceFile, "content");

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "ExtDlgApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "ExtDlgApp"));
            p.UseDialogSet(MsiDialogSet.Minimal, cfg =>
                cfg.InsertStep("CompanyLicenseDlg", StockDialog.Welcome));
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir, [new CompanyLicenseExtension()]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(result.Value, readOnly: true).Value;

        Result<List<string?[]>> dialog = db.QueryRows(
            "SELECT `Dialog` FROM `Dialog` WHERE `Dialog` = 'CompanyLicenseDlg'", fieldCount: 1);
        Assert.True(dialog.IsSuccess);
        Assert.Single(dialog.Value);

        Result<List<string?[]>> control = db.QueryRows(
            "SELECT `Control`, `Type` FROM `Control` WHERE `Dialog_` = 'CompanyLicenseDlg'", fieldCount: 2);
        Assert.True(control.IsSuccess);
        Assert.Contains(control.Value, r => r[0] == "Body" && r[1] == "ScrollableText");
    }
}
