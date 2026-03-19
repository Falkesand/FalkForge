namespace FalkForge.Integration.Tests.DemoEndToEnd;

public static class DemoTestCatalog
{
    private static readonly string DemoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo"));

    private static string DemoProject(string folder, string csproj) =>
        Path.Combine(DemoRoot, folder, csproj);

    private static readonly string[] BaseMsiTables =
        ["File", "Component", "Directory", "Feature"];

    private static string[] WithExtra(params string[] extra)
    {
        var result = new string[BaseMsiTables.Length + extra.Length];
        BaseMsiTables.CopyTo(result, 0);
        extra.CopyTo(result, BaseMsiTables.Length);
        return result;
    }

    public static IReadOnlyList<DemoExpectation> AllDemos { get; } = CreateAll();

    public static IEnumerable<DemoExpectation> MsiDemos =>
        AllDemos.Where(d => d.OutputType == DemoOutputType.Msi);

    public static IEnumerable<DemoExpectation> BundleDemos =>
        AllDemos.Where(d => d.OutputType == DemoOutputType.Bundle);

    public static IEnumerable<object[]> AllDemosData =>
        AllDemos.Select(d => new object[] { d });

    private static List<DemoExpectation> CreateAll() =>
    [
        // MSI demos
        new("01-hello-world",
            DemoProject("01-hello-world", "01-hello-world.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("02-notepad-clone",
            DemoProject("02-notepad-clone", "02-notepad-clone.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("03-client-server",
            DemoProject("03-client-server", "03-client-server.csproj"),
            DemoOutputType.Msi,
            WithExtra("ServiceInstall")),

        new("04-dev-toolkit",
            DemoProject("04-dev-toolkit", "04-dev-toolkit.csproj"),
            DemoOutputType.Msi,
            WithExtra("Environment", "Registry")),

        new("05-enterprise-suite",
            DemoProject("05-enterprise-suite", "05-enterprise-suite.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("07-extensions-showcase",
            DemoProject("07-extensions-showcase", "07-extensions-showcase.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables,
            RequiresInfrastructure: true),

        new("08-localization",
            DemoProject("08-localization", "08-localization.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("09-advanced-msi",
            DemoProject("09-advanced-msi", "09-advanced-msi.csproj"),
            DemoOutputType.Msi,
            WithExtra("CustomAction")),

        // Multi-project MSI demos (sub-projects of 06 and 10)
        new("06-product-suite/app-installer",
            DemoProject("06-product-suite", Path.Combine("app-installer", "app-installer.csproj")),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("06-product-suite/service-installer",
            DemoProject("06-product-suite", Path.Combine("service-installer", "service-installer.csproj")),
            DemoOutputType.Msi,
            WithExtra("ServiceInstall")),

        new("10-advanced-bundle/msi-package",
            DemoProject("10-advanced-bundle", Path.Combine("msi-package", "msi-package.csproj")),
            DemoOutputType.Msi,
            BaseMsiTables),

        // Bundle demos
        new("06-product-suite/suite-bundle",
            DemoProject("06-product-suite", Path.Combine("suite-bundle", "suite-bundle.csproj")),
            DemoOutputType.Bundle,
            []),

        new("10-advanced-bundle/bundle",
            DemoProject("10-advanced-bundle", Path.Combine("bundle", "bundle.csproj")),
            DemoOutputType.Bundle,
            []),
    ];
}
