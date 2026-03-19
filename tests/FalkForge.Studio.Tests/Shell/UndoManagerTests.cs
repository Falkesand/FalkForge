using FalkForge.Studio.Project;
using FalkForge.Studio.Shell;
using Xunit;

namespace FalkForge.Studio.Tests.Shell;

public class UndoManagerTests
{
    private static StudioProject CreateProject(string name = "Test", string manufacturer = "Corp")
    {
        var project = new StudioProject();
        project.Product.Name = name;
        project.Product.Manufacturer = manufacturer;
        return project;
    }

    [Fact]
    public void SaveState_StoresSnapshot_CanUndoIsTrue()
    {
        var manager = new UndoManager();
        var first = CreateProject("First");
        var second = CreateProject("Second");

        manager.SaveState(first);
        manager.SaveState(second);

        Assert.True(manager.CanUndo);
    }

    [Fact]
    public void SaveState_SingleState_CanUndoIsFalse()
    {
        var manager = new UndoManager();
        manager.SaveState(CreateProject());

        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void Undo_ReturnsPreviousState()
    {
        var manager = new UndoManager();
        var first = CreateProject("First");
        var second = CreateProject("Second");

        manager.SaveState(first);
        manager.SaveState(second);

        var result = manager.Undo(second);

        Assert.NotNull(result);
        Assert.Equal("First", result.Product.Name);
    }

    [Fact]
    public void Undo_EmptyStack_ReturnsNull()
    {
        var manager = new UndoManager();

        var result = manager.Undo(CreateProject());

        Assert.Null(result);
    }

    [Fact]
    public void Redo_ReturnsNextState()
    {
        var manager = new UndoManager();
        var first = CreateProject("First");
        var second = CreateProject("Second");

        manager.SaveState(first);
        manager.SaveState(second);

        var undone = manager.Undo(second)!;
        var redone = manager.Redo(undone);

        Assert.NotNull(redone);
        Assert.Equal("Second", redone.Product.Name);
    }

    [Fact]
    public void Redo_EmptyStack_ReturnsNull()
    {
        var manager = new UndoManager();
        manager.SaveState(CreateProject());

        var result = manager.Redo(CreateProject());

        Assert.Null(result);
    }

    [Fact]
    public void Undo_ThenEdit_ClearsRedoStack()
    {
        var manager = new UndoManager();
        var first = CreateProject("First");
        var second = CreateProject("Second");

        manager.SaveState(first);
        manager.SaveState(second);

        var undone = manager.Undo(second)!;
        Assert.True(manager.CanRedo);

        manager.SaveState(CreateProject("Third"));
        Assert.False(manager.CanRedo);
    }

    [Theory]
    [InlineData(51)]
    [InlineData(60)]
    public void SaveState_ExceedsMaxStackSize_TrimsOldest(int count)
    {
        var manager = new UndoManager();

        for (var i = 0; i < count; i++)
            manager.SaveState(CreateProject($"State{i}"));

        var undoCount = 0;
        var current = CreateProject($"State{count - 1}");
        while (manager.CanUndo)
        {
            current = manager.Undo(current)!;
            undoCount++;
        }

        Assert.Equal(50, undoCount);
    }

    [Fact]
    public void SaveState_DuplicateState_NotAdded()
    {
        var manager = new UndoManager();
        var project = CreateProject("Same");

        manager.SaveState(project);
        manager.SaveState(project);
        manager.SaveState(project);

        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void Clear_ResetsAllStacks()
    {
        var manager = new UndoManager();
        var first = CreateProject("First");
        var second = CreateProject("Second");
        var third = CreateProject("Third");

        manager.SaveState(first);
        manager.SaveState(second);
        manager.SaveState(third);
        manager.Undo(third);

        Assert.True(manager.CanUndo);
        Assert.True(manager.CanRedo);

        manager.Clear();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void MultipleUndoRedo_RoundTrips()
    {
        var manager = new UndoManager();
        var states = new StudioProject[5];
        for (var i = 0; i < 5; i++)
            states[i] = CreateProject($"State{i}");

        foreach (var state in states)
            manager.SaveState(state);

        // Undo all the way back
        var current = states[4];
        for (var i = 3; i >= 0; i--)
        {
            current = manager.Undo(current)!;
            Assert.Equal($"State{i}", current.Product.Name);
        }

        Assert.False(manager.CanUndo);

        // Redo all the way forward
        for (var i = 1; i <= 4; i++)
        {
            current = manager.Redo(current)!;
            Assert.Equal($"State{i}", current.Product.Name);
        }

        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void Undo_SetsCanRedoTrue()
    {
        var manager = new UndoManager();
        manager.SaveState(CreateProject("First"));
        manager.SaveState(CreateProject("Second"));

        Assert.False(manager.CanRedo);

        manager.Undo(CreateProject("Second"));

        Assert.True(manager.CanRedo);
    }

    [Fact]
    public void Redo_SetsCanUndoTrue()
    {
        var manager = new UndoManager();
        manager.SaveState(CreateProject("First"));
        manager.SaveState(CreateProject("Second"));

        var undone = manager.Undo(CreateProject("Second"))!;
        var redone = manager.Redo(undone)!;

        Assert.True(manager.CanUndo);
    }

    [Fact]
    public void SaveState_AfterClear_WorksNormally()
    {
        var manager = new UndoManager();
        manager.SaveState(CreateProject("Before"));
        manager.Clear();

        manager.SaveState(CreateProject("After1"));
        manager.SaveState(CreateProject("After2"));

        Assert.True(manager.CanUndo);
        var result = manager.Undo(CreateProject("After2"));
        Assert.NotNull(result);
        Assert.Equal("After1", result.Product.Name);
    }

    [Fact]
    public void StackLimit_ExactlyFifty_DoesNotTrim()
    {
        var manager = new UndoManager();

        // 51 SaveState calls = 50 items on undo stack (first becomes current, then 50 pushes)
        for (var i = 0; i <= 50; i++)
            manager.SaveState(CreateProject($"State{i}"));

        var undoCount = 0;
        var current = CreateProject($"State50");
        while (manager.CanUndo)
        {
            current = manager.Undo(current)!;
            undoCount++;
        }

        Assert.Equal(50, undoCount);
    }
}
