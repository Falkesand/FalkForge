namespace FalkForge.Studio.Project;

public static class ProjectTemplates
{
    public static IReadOnlyList<ProjectTemplate> All { get; } =
    [
        new ProjectTemplate
        {
            Name = "Empty Project",
            Description = "A blank project with minimal configuration.",
            Create = () => new StudioProject
            {
                Product = new ProductSection { Name = "My Application", Manufacturer = "" },
                Features = [new FeatureSection { Id = "Main", Title = "Main Application", IsDefault = true }]
            }
        },
        new ProjectTemplate
        {
            Name = "Desktop Application",
            Description = "A typical desktop app with program files, Start Menu shortcut, and uninstall support.",
            Create = () => new StudioProject
            {
                Product = new ProductSection { Name = "My Desktop App", Manufacturer = "My Company", Version = "1.0.0", Architecture = "x64" },
                Features = [new FeatureSection { Id = "Main", Title = "Application Files", IsDefault = true, IsRequired = true }],
                Shortcuts = [new ShortcutSection { Name = "My Desktop App", TargetFile = "[INSTALLDIR]MyApp.exe", StartMenu = true }]
            }
        },
        new ProjectTemplate
        {
            Name = "Windows Service",
            Description = "A Windows service with automatic startup, plus a management shortcut.",
            Create = () => new StudioProject
            {
                Product = new ProductSection { Name = "My Service", Manufacturer = "My Company", Version = "1.0.0", Scope = "perMachine" },
                Features = [new FeatureSection { Id = "Main", Title = "Service Files", IsDefault = true, IsRequired = true }],
                Services = [new ServiceSection { Name = "MyService", DisplayName = "My Service", Executable = "[INSTALLDIR]MyService.exe", StartMode = "Automatic", Account = "LocalSystem", StartOnInstall = true, StopOnUninstall = true }]
            }
        },
        new ProjectTemplate
        {
            Name = "Web Application",
            Description = "An IIS-hosted web application with app pool and website configuration.",
            Create = () => new StudioProject
            {
                Product = new ProductSection { Name = "My Web App", Manufacturer = "My Company", Version = "1.0.0", Scope = "perMachine" },
                Features = [new FeatureSection { Id = "Main", Title = "Web Application", IsDefault = true, IsRequired = true }]
            }
        },
        new ProjectTemplate
        {
            Name = "EXE Bundle",
            Description = "A multi-package bundle installer (EXE) that chains multiple MSI packages.",
            Create = () => new StudioProject
            {
                ProjectType = "bundle",
                Product = new ProductSection { Name = "My Product Suite", Manufacturer = "My Company", Version = "1.0.0" },
                Features = [new FeatureSection { Id = "Main", Title = "Main", IsDefault = true }],
                BundleSettings = new BundleSettingsSection { Name = "My Product Suite", Manufacturer = "My Company", Version = "1.0.0" }
            }
        }
    ];
}
