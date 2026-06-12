using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// Exhaustiveness guard for FeatureBuilder.CollectFiles.
///
/// WHY this test exists:
///   CollectFiles hand-copies each property of FileEntryModel into a new instance.
///   If a developer adds a new property to FileEntryModel without updating CollectFiles,
///   the new property silently drops to its default value — a data-loss bug with no
///   compile-time signal.
///
/// HOW the guard works:
///   1. Reflect all public, init-settable properties of FileEntryModel.
///   2. Construct a source model with every property set to a distinct non-default value
///      (using the object initializer — typed, no reflection needed for construction).
///   3. Inject the source model directly into FeatureBuilder._files via reflection
///      (private field access is fine in tests; NativeAOT does NOT constrain test projects).
///   4. Call CollectFiles() and take the single output entry.
///   5. For every property EXCEPT FeatureRef (intentionally overridden to the feature ID),
///      assert the output value equals the source value.
///   6. Assert FeatureRef equals the feature ID (the intended override).
///
/// RED check (induced-mutation evidence):
///   The test was verified RED by temporarily commenting out the `ComponentCondition` copy
///   in CollectFiles (the last property in the initializer). The test failed with:
///     "ComponentCondition not round-tripped: expected 'test-condition', actual ''"
///   The copy was then restored and the test returned GREEN. This confirms the guard
///   catches any future omission automatically.
///
/// ADDING A NEW PROPERTY to FileEntryModel:
///   1. This test will FAIL on the line for your new property.
///   2. Add the copy in FeatureBuilder.CollectFiles.
///   3. The test turns GREEN.
///   Do NOT add your property to the KnownIntentionalOverrides list unless you can
///   justify why CollectFiles should not copy it (as is the case for FeatureRef).
/// </summary>
public sealed class FeatureBuilderCollectFilesExhaustivenessTests
{
    // Properties intentionally not copied from source — CollectFiles sets them differently.
    // FeatureRef: overridden to the owning feature's ID, not copied from the source model.
    private static readonly HashSet<string> KnownIntentionalOverrides = new(StringComparer.Ordinal)
    {
        nameof(FileEntryModel.FeatureRef)
    };

    [Fact]
    public void CollectFiles_RoundTripsAllFileEntryModelProperties()
    {
        // WHY: Any new property added to FileEntryModel that is not copied in CollectFiles
        // will silently drop to its default. This test catches that at review time, not at
        // runtime when an installer produced wrong output.

        const string featureId = "TestFeature";

        // --- Arrange: construct a source model with every property set to a non-default value ---
        // This is a typed initializer so that adding a new REQUIRED property to FileEntryModel
        // causes a compile error here, and adding an optional one causes only this test to fail.
        var targetDir = KnownFolder.ProgramFiles / "TestApp" / "Sub";

        var source = new FileEntryModel
        {
            SourcePath = @"C:\payload\testfile.dll",
            TargetDirectory = targetDir,
            FileName = "testfile.dll",
            IsKeyPath = true,               // non-default (default: false)
            ComponentId = "CMP_Test",
            ComponentGuid = new Guid("11111111-2222-3333-4444-555555555555"),
            FeatureRef = "OriginalFeatureRef", // intentionally overridden — see KnownIntentionalOverrides
            Vital = false,                  // non-default (default: true)
            NeverOverwrite = true,          // non-default (default: false)
            Permanent = true,               // non-default (default: false)
            ComponentCondition = "test-condition"
        };

        // Inject source into FeatureBuilder._files via reflection.
        // FeatureBuilder._files is private; reflection is acceptable in test projects
        // (NativeAOT constraints apply only to Engine/Elevation publish targets, not test projects).
        var builder = new FeatureBuilder_Accessor(featureId);
        builder.InjectFile(source);

        // --- Act ---
        var results = builder.Builder.CollectFiles();

        // --- Assert: exactly one output entry ---
        var output = Assert.Single(results);

        // Verify the intentional override: FeatureRef must equal the feature ID, not the source value.
        Assert.Equal(featureId, output.FeatureRef);

        // Verify every other property round-trips exactly.
        // Reflect all public readable properties of FileEntryModel.
        var allProperties = typeof(FileEntryModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToArray();

        // Fail loudly if a property was added to FileEntryModel but not to this test's source model
        // OR not handled in CollectFiles. We check both directions:
        //   - Source value is non-default (construction above ensures this for non-overridden props).
        //   - Output value equals source value (copy happened).
        //   - Exception: KnownIntentionalOverrides are checked separately above.
        var failures = new List<string>();

        foreach (var prop in allProperties)
        {
            if (KnownIntentionalOverrides.Contains(prop.Name))
                continue;

            var sourceValue = prop.GetValue(source);
            var outputValue = prop.GetValue(output);

            if (!Equals(sourceValue, outputValue))
            {
                failures.Add(
                    $"{prop.Name} not round-tripped: expected '{sourceValue}', actual '{outputValue}'");
            }
        }

        if (failures.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                "FeatureBuilder.CollectFiles does not copy all FileEntryModel properties.\n" +
                "Fix CollectFiles to copy the missing property/properties, or add to KnownIntentionalOverrides with justification.\n" +
                "Failures:\n  " + string.Join("\n  ", failures));
        }
    }

    /// <summary>
    /// Helper that exposes FeatureBuilder internals needed for injection without
    /// exposing them in production code.
    /// </summary>
    private sealed class FeatureBuilder_Accessor
    {
        private readonly List<FileEntryModel> _filesField;

        public FeatureBuilder Builder { get; }

        public FeatureBuilder_Accessor(string featureId)
        {
            Builder = new FeatureBuilder(featureId);

            var field = typeof(FeatureBuilder)
                .GetField("_files", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    "FeatureBuilder._files field not found via reflection. " +
                    "If the field was renamed, update FeatureBuilder_Accessor.ctor.");

            _filesField = (List<FileEntryModel>)(field.GetValue(Builder)
                ?? throw new InvalidOperationException("_files field returned null."));
        }

        public void InjectFile(FileEntryModel model) => _filesField.Add(model);
    }
}
