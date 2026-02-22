namespace FalkForge.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? pipeName = null;
        string? manifestPath = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    pipeName = args[++i];
                    break;
                // SECURITY: DEPRECATED — --secret is accepted for backward compatibility but the
                // value is discarded. The engine uses the init-pipe pattern (like Engine.Elevation)
                // to receive secrets over a short-lived pipe instead of command-line arguments,
                // which are visible in process listings and event logs.
                case "--secret":
                    _ = args[++i]; // consume and discard
                    break;
                case "--manifest":
                    manifestPath = args[++i];
                    break;
            }
        }

        // For now, exit with error if no manifest
        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name>]");
            return 1;
        }

        // Manifest loading would happen here (will be implemented with bundle compiler)
        Console.Error.WriteLine("Engine started. Manifest loading not yet implemented.");
        return 0;
    }
}
