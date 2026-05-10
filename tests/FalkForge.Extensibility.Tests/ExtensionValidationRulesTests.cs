using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Extensibility.Tests;

/// <summary>
/// Phase 13 — verifies that extension-contributed <see cref="ValidationRule"/> instances
/// are merged into the engine and fire during <see cref="ModelValidator.Inspect"/>.
/// </summary>
public sealed class ExtensionValidationRulesTests
{
    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    private sealed class AlwaysFailExtension : IFalkForgeExtension
    {
        public string Name => "StubAlwaysFail";

        public void Register(IExtensionRegistry registry) { }

        public ImmutableArray<ValidationRule> GetValidationRules()
            => [Stub001_AlwaysFails];

        internal static readonly ValidationRule Stub001_AlwaysFails = new(
            new RuleId("STUB001"),
            Severity.Error,
            ModelSection.Package,
            "Stub always fails",
            "Test rule — always emits one violation.",
            static ctx => [new Violation(new RuleId("STUB001"), Severity.Error, ModelPath.Root, "stub violation")]);
    }

    private static PackageModel MinimalPackage() => InstallerTestHost.BuildPackage(p =>
    {
        p.Name = "App";
        p.Manufacturer = "Corp";
        p.Version = new Version(1, 0, 0);
        p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
    });

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetValidationRules_DefaultImpl_ReturnsEmpty()
    {
        // IFalkForgeExtension default returns empty — existing extensions unaffected.
        IFalkForgeExtension ext = new DefaultImplExtension();
        Assert.Empty(ext.GetValidationRules());
    }

    [Fact]
    public void GetValidationRules_Override_ReturnsStubRule()
    {
        var ext = new AlwaysFailExtension();
        var rules = ext.GetValidationRules();
        Assert.Single(rules);
        Assert.Equal("STUB001", rules[0].Id.Value);
    }

    [Fact]
    public void RegisterExtensionRules_MakesRuleFireOnInspect()
    {
        // Arrange — extension contributes STUB001 which always fires.
        var ext = new AlwaysFailExtension();
        ModelValidator.RegisterExtensionRules(ext.GetValidationRules());

        var package = MinimalPackage();

        // Act
        var report = ModelValidator.Inspect(package);

        // Assert — STUB001 must appear in report.
        Assert.Contains(report.Violations, v => v.RuleId.Value == "STUB001");
    }

    [Fact]
    public void ListRules_AfterRegisterExtensionRules_IncludesStubRule()
    {
        var ext = new AlwaysFailExtension();
        ModelValidator.RegisterExtensionRules(ext.GetValidationRules());

        var rules = ModelValidator.ListRules();

        Assert.Contains(rules, r => r.Id.Value == "STUB001");
    }

    // Minimal extension that does NOT override GetValidationRules — exercises the default.
    private sealed class DefaultImplExtension : IFalkForgeExtension
    {
        public string Name => "DefaultImpl";
        public void Register(IExtensionRegistry registry) { }
    }
}
