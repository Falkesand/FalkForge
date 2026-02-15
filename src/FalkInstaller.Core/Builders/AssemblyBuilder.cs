namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class AssemblyBuilder
{
    private string _fileRef = string.Empty;
    private AssemblyType _type = AssemblyType.DotNetAssembly;
    private string? _applicationFileRef;
    private string? _name;
    private string? _version;
    private string? _culture;
    private string? _publicKeyToken;
    private string? _architecture;

    public AssemblyBuilder FileRef(string fileRef) { _fileRef = fileRef; return this; }
    public AssemblyBuilder Type(AssemblyType type) { _type = type; return this; }
    public AssemblyBuilder Private(string applicationFileRef) { _applicationFileRef = applicationFileRef; return this; }
    public AssemblyBuilder Name(string name) { _name = name; return this; }
    public AssemblyBuilder Version(string version) { _version = version; return this; }
    public AssemblyBuilder Culture(string culture) { _culture = culture; return this; }
    public AssemblyBuilder PublicKeyToken(string publicKeyToken) { _publicKeyToken = publicKeyToken; return this; }
    public AssemblyBuilder Architecture(string architecture) { _architecture = architecture; return this; }

    internal AssemblyModel Build() => new()
    {
        FileRef = _fileRef,
        Type = _type,
        ApplicationFileRef = _applicationFileRef,
        AssemblyName = _name,
        AssemblyVersion = _version,
        AssemblyCulture = _culture,
        AssemblyPublicKeyToken = _publicKeyToken,
        ProcessorArchitecture = _architecture
    };
}
