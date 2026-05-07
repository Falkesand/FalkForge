using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="ShortcutSchema.Schema"/>.
/// </summary>
public sealed record ShortcutRow(
    string  Shortcut,
    string  Directory_,
    string  Name,
    string  Component_,
    string  Target,
    string? Arguments,
    string? Description,
    int?    Hotkey,
    string? Icon_,
    int?    IconIndex,
    int?    ShowCmd,
    string? WkDir);

/// <summary>
/// Declarative read schema for the MSI <c>Shortcut</c> table.
/// Columns: Shortcut (PK), Directory_, Name, Component_, Target, Arguments,
///          Description, Hotkey, Icon_, IconIndex, ShowCmd, WkDir.
/// </summary>
public static class ShortcutSchema
{
    public static readonly ReadColumn Shortcut    = new("Shortcut",    ReadColumnType.String,  false, 0);
    public static readonly ReadColumn Directory_  = new("Directory_",  ReadColumnType.String,  false, 1);
    public static readonly ReadColumn Name        = new("Name",        ReadColumnType.String,  false, 2);
    public static readonly ReadColumn Component_  = new("Component_",  ReadColumnType.String,  false, 3);
    public static readonly ReadColumn Target      = new("Target",      ReadColumnType.String,  false, 4);
    public static readonly ReadColumn Arguments   = new("Arguments",   ReadColumnType.String,  true,  5);
    public static readonly ReadColumn Description = new("Description", ReadColumnType.String,  true,  6);
    public static readonly ReadColumn Hotkey      = new("Hotkey",      ReadColumnType.Integer, true,  7);
    public static readonly ReadColumn Icon_       = new("Icon_",       ReadColumnType.String,  true,  8);
    public static readonly ReadColumn IconIndex   = new("IconIndex",   ReadColumnType.Integer, true,  9);
    public static readonly ReadColumn ShowCmd     = new("ShowCmd",     ReadColumnType.Integer, true,  10);
    public static readonly ReadColumn WkDir       = new("WkDir",       ReadColumnType.String,  true,  11);

    public static readonly TableReadSchema<ShortcutRow> Schema = new(
        TableName: "Shortcut",
        Columns: [Shortcut, Directory_, Name, Component_, Target, Arguments,
                  Description, Hotkey, Icon_, IconIndex, ShowCmd, WkDir],
        Map: row => Result<ShortcutRow>.Success(new ShortcutRow(
            row.String(Shortcut),
            row.String(Directory_),
            row.String(Name),
            row.String(Component_),
            row.String(Target),
            row.StringOrNull(Arguments),
            row.StringOrNull(Description),
            row.Int32OrNull(Hotkey),
            row.StringOrNull(Icon_),
            row.Int32OrNull(IconIndex),
            row.Int32OrNull(ShowCmd),
            row.StringOrNull(WkDir))));
}
