namespace FalkForge.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? pipeName = null;
        string? sharedSecret = null;
        string? manifestPath = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    pipeName = args[++i];
                    break;
                case "--secret":
                    sharedSecret = args[++i];
                    break;
                case "--manifest":
                    manifestPath = args[++i];
                    break;
            }
        }

        // For now, exit with error if no manifest
        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name> --secret <base64>]");
            return 1;
        }

        // Manifest loading would happen here (will be implemented with bundle compiler)
        Console.Error.WriteLine("Engine started. Manifest loading not yet implemented.");
        return 0;
    }
}
