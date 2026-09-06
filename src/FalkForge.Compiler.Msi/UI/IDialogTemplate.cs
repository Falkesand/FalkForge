using System.Collections.Immutable;

using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI;

internal interface IDialogTemplate
{
    IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package);

    /// <summary>
    /// This set's interactive wizard pages, first page first, ending with the last page before the
    /// install starts. ProgressDlg, ExitDlg, CancelDlg, BrowseDlg and MsiRMFilesInUse are not pages
    /// the user walks through and are not listed.
    /// </summary>
    /// <remarks>
    /// Exposed so <see cref="Layout.DialogFlowSplice"/> can insert extension steps into the real
    /// chain rather than keeping a second copy of it. A second copy would drift, which is the
    /// failure this whole area keeps repeating.
    /// </remarks>
    ImmutableArray<string> StockChain { get; }
}
