using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI;

internal interface IDialogTemplate
{
    IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package);
}
