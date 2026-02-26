namespace FalkForge.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? pipeName = null;
        string? manifestPath = null;
        var planOnly = false;
        string? planOutputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    if (i + 1 < args.Length) pipeName = args[++i];
                    break;
                // SECURITY: DEPRECATED — --secret is accepted for backward compatibility but the
                // value is discarded. The engine uses the init-pipe pattern (like Engine.Elevation)
                // to receive secrets over a short-lived pipe instead of command-line arguments,
                // which are visible in process listings and event logs.
                case "--secret":
                    if (i + 1 < args.Length) _ = args[++i]; // consume and discard
                    break;
                case "--manifest":
                    if (i + 1 < args.Length) manifestPath = args[++i];
                    break;
                case "--plan-only":
                    planOnly = true;
                    break;
                case "--plan-output":
                    if (i + 1 < args.Length) planOutputPath = args[++i];
                    break;
            }
        }

        // For now, exit with error if no manifest
        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name>] [--plan-only [--plan-output <path>]]");
            return 1;
        }

        // Manifest loading and EngineHost wiring will be implemented with bundle compiler.
        // The planOnly / planOutputPath values are parsed above and will be forwarded to
        // EngineHost.IsPlanOnly / EngineHost.PlanOnlyOutputPath once manifest loading lands.
        _ = planOnly;
        _ = planOutputPath;

        Console.Error.WriteLine("Engine started. Manifest loading not yet implemented.");
        return 0;
    }
}
