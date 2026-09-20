namespace FalkForge.Integration.Tests;

using Xunit;
using Xunit.Sdk;

public sealed class SignServerProvisioningTests
{
    [Theory]
    [InlineData(0L, "Error reading property file", "")]
    [InlineData(0L, "", "ERROR loading worker")]
    [InlineData(1L, "worker configuration failed", "")]
    [InlineData(null, "command did not complete", "")]
    public void FailedProvisioning_ReportsCapturedOutput(long? exitCode, string stdout, string stderr)
    {
        var failure = Assert.Throws<TrueException>(() =>
            SignServerProvisioning.AssertSuccess("setproperties", exitCode, stdout, stderr));
        Assert.Contains("setproperties", failure.Message, StringComparison.Ordinal);
        Assert.Contains(stdout + Environment.NewLine + stderr, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulProvisioning_IsAccepted()
    {
        SignServerProvisioning.AssertSuccess("setproperties", 0, "Properties applied", "");
    }
}
