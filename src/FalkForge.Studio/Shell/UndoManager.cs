using System.Text.Json;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed class UndoManager
{
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string? _currentState;
    private const int MaxStackSize = 50;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void SaveState(StudioProject project)
    {
        var json = JsonSerializer.Serialize(project);
        if (json == _currentState) return;

        if (_currentState is not null)
        {
            _undoStack.Push(_currentState);
            if (_undoStack.Count > MaxStackSize)
            {
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                for (var i = 0; i < MaxStackSize; i++)
                    _undoStack.Push(items[i]);
            }
        }
        _redoStack.Clear();
        _currentState = json;
    }

    public StudioProject? Undo(StudioProject current)
    {
        if (_undoStack.Count == 0) return null;

        _redoStack.Push(JsonSerializer.Serialize(current));
        var previousJson = _undoStack.Pop();
        _currentState = previousJson;
        return JsonSerializer.Deserialize<StudioProject>(previousJson);
    }

    public StudioProject? Redo(StudioProject current)
    {
        if (_redoStack.Count == 0) return null;

        _undoStack.Push(JsonSerializer.Serialize(current));
        var nextJson = _redoStack.Pop();
        _currentState = nextJson;
        return JsonSerializer.Deserialize<StudioProject>(nextJson);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _currentState = null;
    }
}
