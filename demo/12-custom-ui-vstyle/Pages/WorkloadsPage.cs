namespace CustomUiVsStyle.Pages;

using System.Collections.ObjectModel;
using CustomUiVsStyle.Models;
using CustomUiVsStyle.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class WorkloadsPage : InstallerPage<WorkloadsView>
{
    private Workload? _selectedWorkload;

    public override string Title => "Workloads";

    public ObservableCollection<Workload> Workloads { get; } = new()
    {
        new Workload
        {
            Name = ".NET Desktop Development",
            Description = "Build WPF, Windows Forms, and console applications using C#, Visual Basic, and F#.",
            Size = "1.2 GB",
            Components = new List<WorkloadComponent>
            {
                new() { Name = ".NET 10 SDK", Size = "450 MB", IsRequired = true },
                new() { Name = "NuGet Package Manager", Size = "25 MB", IsRequired = true },
                new() { Name = "WPF Designer", Size = "180 MB", IsRequired = false },
                new() { Name = "Windows Forms Designer", Size = "120 MB", IsRequired = false },
                new() { Name = "IntelliCode Support", Size = "95 MB", IsRequired = false }
            }
        },
        new Workload
        {
            Name = "Web Development Tools",
            Description = "Build web applications using ASP.NET Core, Blazor, and modern JavaScript frameworks.",
            Size = "890 MB",
            Components = new List<WorkloadComponent>
            {
                new() { Name = "ASP.NET Core Runtime", Size = "210 MB", IsRequired = true },
                new() { Name = "Blazor Templates", Size = "85 MB", IsRequired = true },
                new() { Name = "Browser DevTools Bridge", Size = "45 MB", IsRequired = false },
                new() { Name = "CSS IntelliSense", Size = "30 MB", IsRequired = false }
            }
        },
        new Workload
        {
            Name = "Database Tooling",
            Description = "Design, deploy, and manage SQL Server and PostgreSQL databases with visual query tools.",
            Size = "650 MB",
            Components = new List<WorkloadComponent>
            {
                new() { Name = "SQL Editor", Size = "180 MB", IsRequired = true },
                new() { Name = "Schema Compare", Size = "120 MB", IsRequired = true },
                new() { Name = "Query Profiler", Size = "95 MB", IsRequired = false },
                new() { Name = "PostgreSQL Support", Size = "75 MB", IsRequired = false },
                new() { Name = "Data Migration Wizard", Size = "60 MB", IsRequired = false }
            }
        },
        new Workload
        {
            Name = "Cloud Deployment Kit",
            Description = "Publish and manage applications on Azure, AWS, and Kubernetes clusters.",
            Size = "520 MB",
            Components = new List<WorkloadComponent>
            {
                new() { Name = "Azure SDK", Size = "200 MB", IsRequired = true },
                new() { Name = "Docker Integration", Size = "150 MB", IsRequired = false },
                new() { Name = "Kubernetes Tools", Size = "110 MB", IsRequired = false }
            }
        }
    };

    public Workload? SelectedWorkload
    {
        get => _selectedWorkload;
        set => SetField(ref _selectedWorkload, value);
    }

    public string TotalSelectedSize
    {
        get
        {
            var totalMb = Workloads.Where(w => w.IsSelected).Sum(w => ParseMb(w.Size));
            return totalMb >= 1000 ? $"{totalMb / 1000.0:F1} GB" : $"{totalMb} MB";
        }
    }

    private static int ParseMb(string size)
    {
        var parts = size.Split(' ');
        if (parts.Length == 2 && int.TryParse(parts[0], out var val))
            return parts[1] == "GB" ? val * 1000 : val;
        return 0;
    }

    public void RefreshTotalSize() => OnPropertyChanged(nameof(TotalSelectedSize));

    public override PageResult OnNext()
    {
        if (!Workloads.Any(w => w.IsSelected))
            return PageResult.Stay("Please select at least one workload.");
        return PageResult.Install;
    }
}
