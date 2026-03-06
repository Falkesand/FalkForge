using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ComClassBuilder
{
    private Guid _classId;
    private ComServerType _serverType = ComServerType.InprocServer32;
    private string? _progId;
    private string? _description;
    private ComThreadingModel _threadingModel = ComThreadingModel.Apartment;
    private Guid? _appId;
    private string? _componentRef;

    public ComClassBuilder ClassId(Guid classId) { _classId = classId; return this; }
    public ComClassBuilder InprocServer32() { _serverType = ComServerType.InprocServer32; return this; }
    public ComClassBuilder LocalServer32() { _serverType = ComServerType.LocalServer32; return this; }
    public ComClassBuilder ProgId(string progId) { _progId = progId; return this; }
    public ComClassBuilder Description(string desc) { _description = desc; return this; }
    public ComClassBuilder ThreadingModel(ComThreadingModel model) { _threadingModel = model; return this; }
    public ComClassBuilder AppId(Guid appId) { _appId = appId; return this; }
    public ComClassBuilder ComponentRef(string componentRef) { _componentRef = componentRef; return this; }

    public ComClassModel Build() => new()
    {
        ClassId = _classId,
        ServerType = _serverType,
        ProgId = _progId,
        Description = _description,
        ThreadingModel = _threadingModel,
        AppId = _appId,
        ComponentRef = _componentRef
    };
}
