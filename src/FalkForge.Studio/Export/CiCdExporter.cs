using System.Text;
using FalkForge;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Export;

public enum CiCdPlatform
{
    GitHubActions,
    AzureDevOps,
    Jenkins
}

public static class CiCdExporter
{
    public static Result<string> Export(StudioProject project, CiCdPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(project.Product.Name))
            return Result<string>.Failure(new Error(ErrorKind.Validation, "Product name is required."));

        var projectFile = FileNameSanitizer.Sanitize(project.Product.Name, '-') + ".ffstudio";
        var artifactPattern = project.ProjectType == "bundle" ? "*.exe" : "*.msi";

        return platform switch
        {
            CiCdPlatform.GitHubActions => Result<string>.Success(GenerateGitHubActions(projectFile, artifactPattern)),
            CiCdPlatform.AzureDevOps => Result<string>.Success(GenerateAzureDevOps(projectFile, artifactPattern)),
            CiCdPlatform.Jenkins => Result<string>.Success(GenerateJenkinsfile(projectFile, artifactPattern)),
            _ => Result<string>.Failure(new Error(ErrorKind.Validation, $"Unsupported CI/CD platform: {platform}"))
        };
    }

    private static string GenerateGitHubActions(string projectFile, string artifactPattern)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name: Build Installer");
        sb.AppendLine("on: [push, pull_request]");
        sb.AppendLine("jobs:");
        sb.AppendLine("  build:");
        sb.AppendLine("    runs-on: windows-latest");
        sb.AppendLine("    steps:");
        sb.AppendLine("      - uses: actions/checkout@v4");
        sb.AppendLine("      - uses: actions/setup-dotnet@v4");
        sb.AppendLine("        with:");
        sb.AppendLine("          dotnet-version: '10.0.x'");
        sb.AppendLine("      - run: dotnet tool install -g FalkForge.Cli");
        sb.AppendLine($"      - run: forge build {projectFile}");
        sb.AppendLine("      - uses: actions/upload-artifact@v4");
        sb.AppendLine("        with:");
        sb.AppendLine("          name: installer");
        sb.AppendLine($"          path: '{artifactPattern}'");
        return sb.ToString();
    }

    private static string GenerateAzureDevOps(string projectFile, string artifactPattern)
    {
        var sb = new StringBuilder();
        sb.AppendLine("trigger: [main]");
        sb.AppendLine("pool:");
        sb.AppendLine("  vmImage: 'windows-latest'");
        sb.AppendLine("steps:");
        sb.AppendLine("  - task: UseDotNet@2");
        sb.AppendLine("    inputs:");
        sb.AppendLine("      version: '10.0.x'");
        sb.AppendLine("  - script: dotnet tool install -g FalkForge.Cli");
        sb.AppendLine($"  - script: forge build {projectFile}");
        sb.AppendLine("  - task: PublishBuildArtifacts@1");
        sb.AppendLine("    inputs:");
        sb.AppendLine($"      pathToPublish: '{artifactPattern}'");
        sb.AppendLine("      artifactName: 'installer'");
        return sb.ToString();
    }

    private static string GenerateJenkinsfile(string projectFile, string artifactPattern)
    {
        var sb = new StringBuilder();
        sb.AppendLine("pipeline {");
        sb.AppendLine("    agent { label 'windows' }");
        sb.AppendLine("    stages {");
        sb.AppendLine("        stage('Build') {");
        sb.AppendLine("            steps {");
        sb.AppendLine("                bat 'dotnet tool install -g FalkForge.Cli'");
        sb.AppendLine($"                bat 'forge build {projectFile}'");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    post {");
        sb.AppendLine("        success {");
        sb.AppendLine($"            archiveArtifacts '{artifactPattern}'");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
