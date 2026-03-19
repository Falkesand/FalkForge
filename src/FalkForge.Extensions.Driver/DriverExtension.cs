using FalkForge.Extensibility;

namespace FalkForge.Extensions.Driver;

public sealed class DriverExtension : IFalkForgeExtension, IDryRunContributor
{
    public DriverTableContributor TableContributor { get; } = new();

    public string Name => "Driver";

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(TableContributor);
        registry.RegisterDryRunContributor(this);
    }

    public Result<DriverModel> AddDriver(Action<DriverBuilder> configure)
    {
        var builder = new DriverBuilder();
        configure(builder);
        var result = builder.Build();
        if (result.IsSuccess)
            TableContributor.AddDriver(result.Value);
        return result;
    }

    public IReadOnlyList<DriverValidationError> ValidateDrivers()
    {
        return DriverValidator.Validate(TableContributor.Drivers);
    }

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install =>
            [
                new DryRunAction
                    { Kind = DryRunActionKind.Custom, Description = "Would install device driver(s) via pnputil" }
            ],
            DryRunIntent.Uninstall =>
            [
                new DryRunAction
                    { Kind = DryRunActionKind.Custom, Description = "Would uninstall device driver(s) via pnputil" }
            ],
            _ => []
        };
}
