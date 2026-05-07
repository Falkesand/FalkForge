using System.Xml.Linq;

namespace FalkForge.Decompiler.Tests;

internal sealed class MockWixBurnAccess : IWixBurnAccess
{
    private Guid _bundleId = Guid.NewGuid();
    private Result<XDocument>? _manifestResult;

    public MockWixBurnAccess WithBundleId(Guid id)
    {
        _bundleId = id;
        return this;
    }

    public MockWixBurnAccess WithManifestXml(string xml)
    {
        _manifestResult = Result<XDocument>.Success(XDocument.Parse(xml));
        return this;
    }

    public MockWixBurnAccess WithManifestFailure(ErrorKind kind, string message)
    {
        _manifestResult = Result<XDocument>.Failure(kind, message);
        return this;
    }

    public MockWixBurnAccess WithManifestDocument(XDocument document)
    {
        _manifestResult = Result<XDocument>.Success(document);
        return this;
    }

    public Guid BundleId => _bundleId;

    public Result<XDocument> ReadManifest()
    {
        return _manifestResult ?? Result<XDocument>.Failure(ErrorKind.BundleError, "No manifest configured in mock.");
    }

    public void Dispose()
    {
        // No-op for mock
    }
}
