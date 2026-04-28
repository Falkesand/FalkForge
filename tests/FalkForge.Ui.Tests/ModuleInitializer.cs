using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace FalkForge.Ui.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }
}
