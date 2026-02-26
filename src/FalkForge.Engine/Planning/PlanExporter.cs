using System.Text;
using System.Text.Json;

namespace FalkForge.Engine.Planning;

internal static class PlanExporter
{
    internal static string ToJson(InstallPlan plan)
    {
        var output = BuildPlanOutput(plan);
        return JsonSerializer.Serialize(output, PlanJsonContext.Default.PlanOutput);
    }

    internal static Result<Unit> WriteToFile(InstallPlan plan, string filePath)
    {
        try
        {
            var json = ToJson(plan);
            File.WriteAllText(filePath, json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Plan export failed: {ex.Message}");
        }
    }

    private static PlanOutput BuildPlanOutput(InstallPlan plan)
    {
        var packages = new PlanActionOutput[plan.Actions.Count];
        for (var i = 0; i < plan.Actions.Count; i++)
        {
            var action = plan.Actions[i];
            packages[i] = new PlanActionOutput
            {
                PackageId = action.PackageId,
                Action = action.ActionType.ToString()
            };
        }

        return new PlanOutput
        {
            PlanVersion = "1",
            GeneratedAt = DateTimeOffset.UtcNow.ToString("o"),
            Packages = packages
        };
    }
}
