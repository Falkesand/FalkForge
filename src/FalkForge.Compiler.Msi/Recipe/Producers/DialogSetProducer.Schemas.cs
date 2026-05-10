using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Tables;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Schema builder methods for each MSI dialog-related table.
/// These are pure data — no side effects — and are separated here to
/// keep <see cref="DialogSetProducer"/> focused on production logic.
/// </summary>
internal sealed partial class DialogSetProducer
{
    // ── Schema builders ───────────────────────────────────────────────────────

    private static TableSchema BuildDialogSchema()
    {
        // Dialog DDL: `Dialog` CHAR(72) NN, `HCentering` SHORT NN, `VCentering` SHORT NN,
        // `Width` SHORT NN, `Height` SHORT NN, `Attributes` LONG, `Title` CHAR(128) LOC,
        // `Control_First` CHAR(50) NN, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50)
        // PRIMARY KEY `Dialog`
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog",          Type = ColumnType.String,  Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "HCentering",      Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "VCentering",      Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Width",           Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Height",          Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Attributes",      Type = ColumnType.Integer, Width = 4,   Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Title",           Type = ColumnType.String,  Width = 128, Nullable = true,  LocalizableKey = true  },
            new RecipeColumn { Name = "Control_First",   Type = ColumnType.String,  Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_Default", Type = ColumnType.String,  Width = 50,  Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Control_Cancel",  Type = ColumnType.String,  Width = 50,  Nullable = true,  LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("Dialog").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildControlSchema()
    {
        // Control DDL: Dialog_ CHAR(72) NN, Control CHAR(50) NN, Type CHAR(20) NN,
        // X SHORT NN, Y SHORT NN, Width SHORT NN, Height SHORT NN, Attributes LONG,
        // Property CHAR(50), Text LONGCHAR LOC, Control_Next CHAR(50), Help CHAR(255) LOC
        // PRIMARY KEY Dialog_, Control
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",       Type = ColumnType.String,  Width = 72,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control",       Type = ColumnType.String,  Width = 50,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Type",          Type = ColumnType.String,  Width = 20,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "X",             Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Y",             Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Width",         Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Height",        Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Attributes",    Type = ColumnType.Integer, Width = 4,    Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Property",      Type = ColumnType.String,  Width = 50,   Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Text",          Type = ColumnType.String,  Width = 0,    Nullable = true,  LocalizableKey = true  }, // LONGCHAR
            new RecipeColumn { Name = "Control_Next",  Type = ColumnType.String,  Width = 50,   Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Help",          Type = ColumnType.String,  Width = 255,  Nullable = true,  LocalizableKey = true  });

        return new TableSchema
        {
            Name = TableId.Create("Control").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildControlEventSchema()
    {
        // ControlEvent DDL: Dialog_ CHAR(72) NN, Control_ CHAR(50) NN,
        // Event CHAR(50) NN, Argument CHAR(255) NN, Condition CHAR(255), Ordering SHORT
        // PRIMARY KEY Dialog_, Control_, Event, Argument, Condition
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",   Type = ColumnType.String,  Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_",  Type = ColumnType.String,  Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Event",     Type = ColumnType.String,  Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Argument",  Type = ColumnType.String,  Width = 255, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Condition", Type = ColumnType.String,  Width = 255, Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Ordering",  Type = ColumnType.Integer, Width = 2,   Nullable = true,  LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("ControlEvent").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0), new ColumnIndex(1), new ColumnIndex(2),
                new ColumnIndex(3), new ColumnIndex(4)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildControlConditionSchema()
    {
        // ControlCondition DDL: Dialog_ CHAR(72) NN, Control_ CHAR(50) NN,
        // Action CHAR(50) NN, Condition CHAR(255) NN
        // PRIMARY KEY Dialog_, Control_, Action, Condition
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",   Type = ColumnType.String, Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_",  Type = ColumnType.String, Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Action",    Type = ColumnType.String, Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Condition", Type = ColumnType.String, Width = 255, Nullable = false, LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("ControlCondition").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0), new ColumnIndex(1),
                new ColumnIndex(2), new ColumnIndex(3)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildEventMappingSchema()
    {
        // EventMapping DDL: Dialog_ CHAR(72) NN, Control_ CHAR(50) NN,
        // Event CHAR(50) NN, Attribute CHAR(50) NN
        // PRIMARY KEY Dialog_, Control_, Event
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",   Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_",  Type = ColumnType.String, Width = 50, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Event",     Type = ColumnType.String, Width = 50, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Attribute", Type = ColumnType.String, Width = 50, Nullable = false, LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("EventMapping").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0), new ColumnIndex(1), new ColumnIndex(2)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildTextStyleSchema()
    {
        // TextStyle DDL: TextStyle CHAR(72) NN, FaceName CHAR(32) NN,
        // Size SHORT NN, Color LONG, StyleBits SHORT
        // PRIMARY KEY TextStyle
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "TextStyle",  Type = ColumnType.String,  Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "FaceName",   Type = ColumnType.String,  Width = 32, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Size",       Type = ColumnType.Integer, Width = 2,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Color",      Type = ColumnType.Integer, Width = 4,  Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "StyleBits",  Type = ColumnType.Integer, Width = 2,  Nullable = true,  LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("TextStyle").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildUITextSchema()
    {
        // UIText DDL: Key CHAR(72) NN, Text CHAR(255) LOC
        // PRIMARY KEY Key
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Key",  Type = ColumnType.String, Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Text", Type = ColumnType.String, Width = 255, Nullable = true,  LocalizableKey = true  });

        return new TableSchema
        {
            Name = TableId.Create("UIText").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
