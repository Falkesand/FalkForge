using FalkInstaller.Extensions.Iis.Models;

namespace FalkInstaller.Extensions.Iis.Builders;

public sealed class CertificateBuilder
{
    private string _id = string.Empty;
    private CertificateStoreName _storeName = CertificateStoreName.My;
    private CertificateStoreLocation _storeLocation = CertificateStoreLocation.LocalMachine;
    private CertificateFindType _findType = CertificateFindType.FindByThumbprint;
    private string _findValue = string.Empty;
    private bool _exportable;

    public CertificateBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public CertificateBuilder Store(CertificateStoreName storeName, CertificateStoreLocation storeLocation)
    {
        _storeName = storeName;
        _storeLocation = storeLocation;
        return this;
    }

    public CertificateBuilder FindByThumbprint(string thumbprint)
    {
        _findType = CertificateFindType.FindByThumbprint;
        _findValue = thumbprint;
        return this;
    }

    public CertificateBuilder FindBySubjectName(string subjectName)
    {
        _findType = CertificateFindType.FindBySubjectName;
        _findValue = subjectName;
        return this;
    }

    public CertificateBuilder Exportable()
    {
        _exportable = true;
        return this;
    }

    internal CertificateModel Build() => new()
    {
        Id = _id,
        StoreName = _storeName,
        StoreLocation = _storeLocation,
        FindType = _findType,
        FindValue = _findValue,
        Exportable = _exportable
    };
}
