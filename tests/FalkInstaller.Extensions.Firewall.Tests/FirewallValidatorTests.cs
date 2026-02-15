using Xunit;

namespace FalkInstaller.Extensions.Firewall.Tests;

public sealed class FirewallValidatorTests
{
    [Fact]
    public void Validate_MissingName_ProducesFWL001()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "",
                Port = "80"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL001");
    }

    [Fact]
    public void Validate_NoPortOrProgram_ProducesFWL002()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL002");
    }

    [Fact]
    public void Validate_InvalidPortFormat_ProducesFWL003()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule",
                Port = "abc"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL003");
    }

    [Theory]
    [InlineData("not-a-port")]
    [InlineData("80-")]
    [InlineData("-80")]
    [InlineData("80-90-100")]
    [InlineData("abc-def")]
    public void Validate_VariousInvalidPortFormats_ProduceFWL003(string invalidPort)
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule",
                Port = invalidPort
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL003");
    }

    [Theory]
    [InlineData("80")]
    [InlineData("443")]
    [InlineData("8080-8090")]
    [InlineData("1-65535")]
    public void Validate_ValidPortFormats_NoFWL003(string validPort)
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule",
                Port = validPort
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.DoesNotContain(errors, e => e.Code == "FWL003");
    }

    [Fact]
    public void Validate_ValidRuleWithPort_NoErrors()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Web Server",
                Port = "80",
                Protocol = FirewallProtocol.Tcp,
                Direction = FirewallDirection.Inbound,
                Action = FirewallRuleAction.Allow
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidRuleWithProgram_NoErrors()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "App Server",
                Program = "[INSTALLFOLDER]server.exe",
                Protocol = FirewallProtocol.Tcp,
                Direction = FirewallDirection.Inbound,
                Action = FirewallRuleAction.Allow
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_PortZero_ProducesFWL003()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule",
                Port = "0"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL003");
    }

    [Theory]
    [InlineData("65536")]
    [InlineData("99999")]
    public void Validate_PortAbove65535_ProducesFWL003(string invalidPort)
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule",
                Port = invalidPort
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL003");
    }

    [Fact]
    public void Validate_DescendingPortRange_ProducesFWL003()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Test Rule",
                Port = "80-70"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL003");
    }

    [Fact]
    public void Validate_DuplicateRuleIds_ProducesFWL004()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Rule A",
                Port = "80"
            },
            new()
            {
                Id = "FW1",
                Name = "Rule B",
                Port = "443"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.Contains(errors, e => e.Code == "FWL004");
    }

    [Fact]
    public void Validate_UniqueRuleIds_NoDuplicateError()
    {
        var rules = new List<FirewallRuleModel>
        {
            new()
            {
                Id = "FW1",
                Name = "Rule A",
                Port = "80"
            },
            new()
            {
                Id = "FW2",
                Name = "Rule B",
                Port = "443"
            }
        };

        var errors = FirewallValidator.Validate(rules);

        Assert.DoesNotContain(errors, e => e.Code == "FWL004");
    }
}
