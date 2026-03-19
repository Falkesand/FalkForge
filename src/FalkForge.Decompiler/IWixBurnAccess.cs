using System.Xml.Linq;
using FalkForge;

namespace FalkForge.Decompiler;

/// <summary>
/// Abstracts WiX Burn bundle file reading for testability.
/// Follows the <see cref="IBundleAccess"/> pattern for FALKBUNDLE format.
/// </summary>
public interface IWixBurnAccess : IDisposable
{
    /// <summary>
    /// Gets the bundle identifier from the <c>.wixburn</c> PE section.
    /// </summary>
    Guid BundleId { get; }

    /// <summary>
    /// Reads the Burn manifest XML from the bundle.
    /// </summary>
    /// <returns>The manifest document if successful; otherwise a failure result.</returns>
    Result<XDocument> ReadManifest();
}
