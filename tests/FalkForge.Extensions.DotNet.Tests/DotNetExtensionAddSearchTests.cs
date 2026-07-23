using Xunit;

namespace FalkForge.Extensions.DotNet.Tests;

public sealed class DotNetExtensionAddSearchTests
{
    [Fact]
    public void AddSearch_NullModel_ReturnsNET006Failure()
    {
        // Repo convention is Result<T>/ErrorKind for recoverable input errors, not exceptions — a
        // caller passing a null model (e.g. from a deserialized/optional source) must get a typed
        // failure it can branch on, not an unhandled ArgumentNullException.
        var extension = new DotNetExtension();

        var result = extension.AddSearch(null!);

        Assert.True(result.IsFailure);
        Assert.Contains("NET006", result.Error.Message);
    }
}
