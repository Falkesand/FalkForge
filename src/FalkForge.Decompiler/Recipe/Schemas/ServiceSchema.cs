using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="ServiceSchema.Schema"/>.
/// </summary>
public sealed record ServiceRow(
    string  ServiceInstall,
    string  Name,
    string? DisplayName,
    int     ServiceType,
    int     StartType,
    int     ErrorControl,
    string? LoadOrderGroup,
    string? Dependencies,
    string? StartName,
    string? Password,
    string? Arguments,
    string  Component_,
    string? Description_);

/// <summary>
/// Declarative read schema for the MSI <c>ServiceInstall</c> table.
/// Columns: ServiceInstall (PK), Name, DisplayName, ServiceType, StartType, ErrorControl,
///          LoadOrderGroup, Dependencies, StartName, Password, Arguments, Component_, Description_.
/// </summary>
public static class ServiceSchema
{
    public static readonly ReadColumn ServiceInstall  = new("ServiceInstall",  ReadColumnType.String,  false, 0);
    public static readonly ReadColumn Name            = new("Name",            ReadColumnType.String,  false, 1);
    public static readonly ReadColumn DisplayName     = new("DisplayName",     ReadColumnType.String,  true,  2);
    public static readonly ReadColumn ServiceType     = new("ServiceType",     ReadColumnType.Integer, false, 3);
    public static readonly ReadColumn StartType       = new("StartType",       ReadColumnType.Integer, false, 4);
    public static readonly ReadColumn ErrorControl    = new("ErrorControl",    ReadColumnType.Integer, false, 5);
    public static readonly ReadColumn LoadOrderGroup  = new("LoadOrderGroup",  ReadColumnType.String,  true,  6);
    public static readonly ReadColumn Dependencies    = new("Dependencies",    ReadColumnType.String,  true,  7);
    public static readonly ReadColumn StartName       = new("StartName",       ReadColumnType.String,  true,  8);
    public static readonly ReadColumn Password        = new("Password",        ReadColumnType.String,  true,  9);
    public static readonly ReadColumn Arguments       = new("Arguments",       ReadColumnType.String,  true,  10);
    public static readonly ReadColumn Component_      = new("Component_",      ReadColumnType.String,  false, 11);
    public static readonly ReadColumn Description_    = new("Description_",    ReadColumnType.String,  true,  12);

    public static readonly TableReadSchema<ServiceRow> Schema = new(
        TableName: "ServiceInstall",
        Columns: [ServiceInstall, Name, DisplayName, ServiceType, StartType, ErrorControl,
                  LoadOrderGroup, Dependencies, StartName, Password, Arguments, Component_, Description_],
        Map: row => Result<ServiceRow>.Success(new ServiceRow(
            row.String(ServiceInstall),
            row.String(Name),
            row.StringOrNull(DisplayName),
            row.Int32(ServiceType),
            row.Int32(StartType),
            row.Int32(ErrorControl),
            row.StringOrNull(LoadOrderGroup),
            row.StringOrNull(Dependencies),
            row.StringOrNull(StartName),
            row.StringOrNull(Password),
            row.StringOrNull(Arguments),
            row.String(Component_),
            row.StringOrNull(Description_))));
}
