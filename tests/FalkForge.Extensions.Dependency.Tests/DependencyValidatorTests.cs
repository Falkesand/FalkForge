using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyValidatorTests
{
    [Fact]
    public void Validate_ValidProviderAndConsumer_NoErrors()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "MyApp", ConsumerKey = "OtherApp", MinVersion = "1.0.0" }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyProviderKey_ReturnsDEP001()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "", Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>();

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP001");
    }

    [Fact]
    public void Validate_InvalidProviderVersion_ReturnsDEP002()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "not-a-version" }
        };
        var consumers = new List<DependencyConsumerModel>();

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP002");
    }

    [Fact]
    public void Validate_EmptyConsumerProviderKey_ReturnsDEP003()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "", ConsumerKey = "OtherApp" }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP003");
    }

    [Fact]
    public void Validate_MinGreaterThanMax_ReturnsDEP004()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "MyApp",
                ConsumerKey = "OtherApp",
                MinVersion = "2.0.0",
                MaxVersion = "1.0.0"
            }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP004");
    }

    [Fact]
    public void Validate_DuplicateProviderKeys_ReturnsDEP005()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "1.0.0" },
            new() { Key = "MyApp", Version = "2.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>();

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP005");
    }

    [Fact]
    public void Validate_ValidVersionRange_NoErrors()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "MyApp",
                ConsumerKey = "OtherApp",
                MinVersion = "1.0.0",
                MaxVersion = "2.0.0"
            }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.DoesNotContain(errors, e => e.Code == "DEP004");
    }

    [Fact]
    public void Validate_MultipleErrors_ReturnsAll()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "", Version = "not-a-version" },
            new() { Key = "Dup", Version = "1.0.0" },
            new() { Key = "Dup", Version = "2.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new()
            {
                ProviderKey = "",
                ConsumerKey = "X",
                MinVersion = "3.0.0",
                MaxVersion = "1.0.0"
            }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP001");
        Assert.Contains(errors, e => e.Code == "DEP002");
        Assert.Contains(errors, e => e.Code == "DEP003");
        Assert.Contains(errors, e => e.Code == "DEP004");
        Assert.Contains(errors, e => e.Code == "DEP005");
    }

    [Theory]
    [InlineData(@"My\App")]
    [InlineData("My/App")]
    [InlineData("My\0App")]
    public void Validate_ProviderKeyWithInvalidChars_ReturnsDEP006(string key)
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = key, Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>();

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP006");
    }

    [Theory]
    [InlineData(@"My\Provider")]
    [InlineData("My/Provider")]
    [InlineData("My\0Provider")]
    public void Validate_ConsumerProviderKeyWithInvalidChars_ReturnsDEP006(string providerKey)
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = providerKey, ConsumerKey = "ValidConsumer" }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP006");
    }

    [Theory]
    [InlineData(@"My\Consumer")]
    [InlineData("My/Consumer")]
    [InlineData("My\0Consumer")]
    public void Validate_ConsumerKeyWithInvalidChars_ReturnsDEP006(string consumerKey)
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "ValidProvider", ConsumerKey = consumerKey }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP006");
    }

    [Fact]
    public void Validate_EmptyConsumerKey_ReturnsDEP007()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "MyApp", ConsumerKey = "" }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP007");
    }

    [Fact]
    public void Validate_WhitespaceConsumerKey_ReturnsDEP007()
    {
        var providers = new List<DependencyProviderModel>();
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "MyApp", ConsumerKey = "   " }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.Contains(errors, e => e.Code == "DEP007");
    }

    [Fact]
    public void Validate_ValidKeys_NoDEP006()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "Contoso.MyApp", Version = "1.0.0" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "Contoso.MyApp", ConsumerKey = "Contoso.OtherApp" }
        };

        var errors = DependencyValidator.Validate(providers, consumers);

        Assert.DoesNotContain(errors, e => e.Code == "DEP006");
    }
}
