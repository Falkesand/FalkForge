using FalkForge.Compiler.Msi.UI.Layout.Builders;
using System.Collections.Immutable;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Tests that <see cref="MsiAuthoring.Compile"/> wires extension-contributed
/// <see cref="IMsiDialogStepBuilder"/> instances into the <see cref="DialogStepRegistry"/>
/// before DLG001 validation runs, so that a <see cref="DialogCustomizationModel.InsertedSteps"/>
/// referencing an extension step does not produce a false DLG001 error.
/// RFC Cycle 6, step 16.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAuthoringDialogStepRegistrationTests : IDisposable
{
    private readonly string _tempDir;

    public MsiAuthoringDialogStepRegistrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiAuthoringDlgStep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            TestTemp.TryDelete(_tempDir);
        }
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal MSI dialog step builder for use in tests. Produces an almost-empty
    /// <see cref="MsiDialogModel"/> sufficient for registry participation.
    /// </summary>
    private sealed class StubMsiDialogStepBuilder(string name) : IMsiDialogStepBuilder
    {
        public string Name => name;

        public int BuildCallCount { get; private set; }

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            BuildCallCount++;
            var model = new MsiDialogModel { Name = Name, FirstControl = "Next" };

            // A step is now spliced into the wizard chain, so it must publish the forward edge it
            // was handed. Before that it was emitted as a dialog nothing navigated to, and a
            // builder with no events described a page the user could not advance past.
            DialogControlEvent next = DialogFooter.NextEvent(context.Flow);
            model.Controls.Add(new MsiControlModel
            {
                Name = "Next", Type = MsiControlType.PushButton,
                X = 280, Y = 240, Width = 66, Height = 17, Text = "Next",
            });
            model.Events.Add(new MsiControlEventModel
            {
                DialogName = Name,
                ControlName = "Next",
                Event = MsiControlEvent.Parse(next.Event),
                Argument = next.Argument,
            });
            return model;
        }
    }

    /// <summary>
    /// Extension that registers one dialog step builder during <see cref="Register"/>.
    /// </summary>
    private sealed class StubDialogStepExtension(StubMsiDialogStepBuilder builder) : IFalkForgeExtension
    {
        public string Name => "StubDialogStepExtension";
        public string Version => "1.0.0";
        public string MinHostVersion => "0.0.0";

        public void Register(IExtensionRegistry registry)
            => registry.RegisterDialogStep(builder);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compile_extension_registers_dialog_step_DLG001_does_not_fire()
    {
        // Arrange — package with a dialog customization that inserts the extension
        // step. Without wiring, DLG001 would fire (step not in registry).
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.txt");
        File.WriteAllText(sourceFile, "content");

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        var stepBuilder = new StubMsiDialogStepBuilder("CompanyLicenseDlg");
        var extension = new StubDialogStepExtension(stepBuilder);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "DlgStepTest";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "DlgStepTest"));
            p.UseDialogSet(MsiDialogSet.Minimal, cfg =>
                cfg.InsertStep("CompanyLicenseDlg", DialogStepAnchor.Welcome));
        });

        // Act
        Result<string> result = MsiAuthoring.Compile(package, outputDir, [extension]);

        // Assert — compilation must succeed (DLG001 must NOT fire)
        Assert.True(result.IsSuccess,
            result.IsFailure ? $"Unexpected failure: {result.Error.Message}" : null);
    }

    [Fact]
    public void Compile_unregistered_insert_step_still_produces_DLG001()
    {
        // Arrange — package references "UnknownStep" but no extension registers it.
        string sourceDir = Path.Combine(_tempDir, "src2");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.txt");
        File.WriteAllText(sourceFile, "content");

        string outputDir = Path.Combine(_tempDir, "out2");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "DlgStepTest2";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "DlgStepTest2"));
            p.UseDialogSet(MsiDialogSet.Minimal, cfg =>
                cfg.InsertStep("UnknownStep", DialogStepAnchor.Welcome));
        });

        // Act — no extensions registered
        Result<string> result = MsiAuthoring.Compile(package, outputDir, []);

        // Assert — DLG001 must fire
        Assert.True(result.IsFailure, "Expected DLG001 validation failure but compilation succeeded.");
        Assert.Contains("DLG001", result.Error.Message);
    }
}
