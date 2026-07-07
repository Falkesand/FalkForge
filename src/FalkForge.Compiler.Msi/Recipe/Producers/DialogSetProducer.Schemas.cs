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
            RecipeColumn.String("Dialog", 72),
            RecipeColumn.Integer("HCentering", 2),
            RecipeColumn.Integer("VCentering", 2),
            RecipeColumn.Integer("Width", 2),
            RecipeColumn.Integer("Height", 2),
            RecipeColumn.Integer("Attributes", 4, nullable: true),
            RecipeColumn.String("Title", 128, nullable: true, localizableKey: true),
            RecipeColumn.String("Control_First", 50),
            RecipeColumn.String("Control_Default", 50, nullable: true),
            RecipeColumn.String("Control_Cancel", 50, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.Dialog,
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
            RecipeColumn.String("Dialog_", 72),
            RecipeColumn.String("Control", 50),
            RecipeColumn.String("Type", 20),
            RecipeColumn.Integer("X", 2),
            RecipeColumn.Integer("Y", 2),
            RecipeColumn.Integer("Width", 2),
            RecipeColumn.Integer("Height", 2),
            RecipeColumn.Integer("Attributes", 4, nullable: true),
            RecipeColumn.String("Property", 50, nullable: true),
            RecipeColumn.String("Text", 0, nullable: true, localizableKey: true), // LONGCHAR
            RecipeColumn.String("Control_Next", 50, nullable: true),
            RecipeColumn.String("Help", 255, nullable: true, localizableKey: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.Control,
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
            RecipeColumn.String("Dialog_", 72),
            RecipeColumn.String("Control_", 50),
            RecipeColumn.String("Event", 50),
            RecipeColumn.String("Argument", 255),
            RecipeColumn.String("Condition", 255, nullable: true),
            RecipeColumn.Integer("Ordering", 2, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.ControlEvent,
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
            RecipeColumn.String("Dialog_", 72),
            RecipeColumn.String("Control_", 50),
            RecipeColumn.String("Action", 50),
            RecipeColumn.String("Condition", 255));

        return new TableSchema
        {
            Name = WellKnownTableIds.ControlCondition,
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
            RecipeColumn.String("Dialog_", 72),
            RecipeColumn.String("Control_", 50),
            RecipeColumn.String("Event", 50),
            RecipeColumn.String("Attribute", 50));

        return new TableSchema
        {
            Name = WellKnownTableIds.EventMapping,
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
            RecipeColumn.String("TextStyle", 72),
            RecipeColumn.String("FaceName", 32),
            RecipeColumn.Integer("Size", 2),
            RecipeColumn.Integer("Color", 4, nullable: true),
            RecipeColumn.Integer("StyleBits", 2, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.TextStyle,
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
            RecipeColumn.String("Key", 72),
            RecipeColumn.String("Text", 255, nullable: true, localizableKey: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.UIText,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
