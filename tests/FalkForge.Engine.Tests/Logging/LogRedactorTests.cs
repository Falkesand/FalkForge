namespace FalkForge.Engine.Tests.Logging;

using FalkForge.Engine.Logging;
using Xunit;

/// <summary>
/// Direct unit tests for <see cref="LogRedactor.Redact"/>'s zero-allocation passthrough
/// contract: when there is nothing to redact, the method must return the exact same
/// dictionary instance it was given (not a copy), so callers on the hot no-secret path pay
/// no allocation cost. <see cref="EngineLoggerTests"/> covers the end-to-end masking
/// behaviour through <see cref="EngineLogger"/>; these tests pin the passthrough contract
/// directly against <see cref="LogRedactor"/> so a regression that made <c>Redact</c> always
/// copy would be caught here even if the end-to-end masking assertions still passed.
/// </summary>
public sealed class LogRedactorTests
{
    [Fact]
    public void Redact_NullProperties_ReturnsNull()
    {
        var result = LogRedactor.Redact(null, LogRedactor.DefaultSecretKeyTokens);

        Assert.Null(result);
    }

    [Fact]
    public void Redact_NoSecretMatchingKey_ReturnsSameInstance()
    {
        var properties = new Dictionary<string, string> { ["PackageId"] = "MyApp" };

        var result = LogRedactor.Redact(properties, LogRedactor.DefaultSecretKeyTokens);

        Assert.Same(properties, result);
    }

    [Fact]
    public void Redact_EmptyTokenList_ReturnsSameInstance()
    {
        // WHY: an empty deny-list means nothing can ever match, even if the properties
        // dictionary contains a key that would otherwise match the default deny-list —
        // the token list, not the property keys, decides whether there is a copy.
        var properties = new Dictionary<string, string> { ["Password"] = "hunter2" };

        var result = LogRedactor.Redact(properties, []);

        Assert.Same(properties, result);
    }
}
