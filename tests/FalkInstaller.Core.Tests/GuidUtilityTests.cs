using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class GuidUtilityTests
{
    [Fact]
    public void CreateDeterministicGuid_SameInput_ReturnsSameGuid()
    {
        var ns = GuidUtility.FalkInstallerNamespace;

        var guid1 = GuidUtility.CreateDeterministicGuid(ns, "test-name");
        var guid2 = GuidUtility.CreateDeterministicGuid(ns, "test-name");

        Assert.Equal(guid1, guid2);
    }

    [Fact]
    public void CreateDeterministicGuid_DifferentNames_ReturnsDifferentGuids()
    {
        var ns = GuidUtility.FalkInstallerNamespace;

        var guid1 = GuidUtility.CreateDeterministicGuid(ns, "name-a");
        var guid2 = GuidUtility.CreateDeterministicGuid(ns, "name-b");

        Assert.NotEqual(guid1, guid2);
    }

    [Fact]
    public void CreateDeterministicGuid_DifferentNamespaces_ReturnsDifferentGuids()
    {
        var ns1 = GuidUtility.FalkInstallerNamespace;
        var ns2 = Guid.NewGuid();

        var guid1 = GuidUtility.CreateDeterministicGuid(ns1, "same-name");
        var guid2 = GuidUtility.CreateDeterministicGuid(ns2, "same-name");

        Assert.NotEqual(guid1, guid2);
    }

    [Fact]
    public void CreateDeterministicGuid_HasVersion5BitsSet()
    {
        var guid = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, "version-test");

        // Version is stored in bits 4-7 of the 7th byte (index 6 in big-endian)
        // In the GUID string format: xxxxxxxx-xxxx-Vxxx-xxxx-xxxxxxxxxxxx
        // Version 5 means the first hex digit of the third group is '5'
        var guidStr = guid.ToString();
        var versionChar = guidStr.Split('-')[2][0];
        Assert.Equal('5', versionChar);
    }

    [Fact]
    public void CreateDeterministicGuid_HasRfc4122VariantBitsSet()
    {
        var guid = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, "variant-test");

        // Variant bits are in the first hex digit of the 4th group: must be 8, 9, a, or b
        var guidStr = guid.ToString();
        var variantChar = guidStr.Split('-')[3][0];
        Assert.Contains(variantChar, new[] { '8', '9', 'a', 'b' });
    }

    [Fact]
    public void FalkInstallerNamespace_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, GuidUtility.FalkInstallerNamespace);
    }

    [Fact]
    public void CreateDeterministicGuid_EmptyString_ProducesValidGuid()
    {
        var guid = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, "");

        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void CreateDeterministicGuid_LongInput_ProducesValidGuid()
    {
        var longName = new string('x', 10000);

        var guid = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, longName);

        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void CreateDeterministicGuid_IsStableAcrossInvocations()
    {
        // A known input should always produce the exact same GUID
        var guid1 = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, "stable-test");
        var guid2 = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, "stable-test");
        var guid3 = GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, "stable-test");

        Assert.Equal(guid1, guid2);
        Assert.Equal(guid2, guid3);
    }
}
