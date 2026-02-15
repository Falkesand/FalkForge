namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class SequenceBuilder
{
    private readonly SequenceTable _table;
    private readonly List<SequenceActionModel> _actions = [];
    private string? _currentActionName;
    private ActionPosition? _currentPosition;
    private string? _currentCondition;

    internal SequenceBuilder(SequenceTable table)
    {
        _table = table;
    }

    public SequenceBuilder Action(string actionName)
    {
        FlushCurrent();
        _currentActionName = actionName;
        _currentPosition = null;
        _currentCondition = null;
        return this;
    }

    public SequenceBuilder After(string referenceAction)
    {
        _currentPosition = new ActionPosition.AfterAction(referenceAction);
        return this;
    }

    public SequenceBuilder Before(string referenceAction)
    {
        _currentPosition = new ActionPosition.BeforeAction(referenceAction);
        return this;
    }

    public SequenceBuilder At(int sequenceNumber)
    {
        _currentPosition = new ActionPosition.AtNumber(sequenceNumber);
        return this;
    }

    public SequenceBuilder Condition(string condition)
    {
        _currentCondition = condition;
        return this;
    }

    internal Result<List<SequenceActionModel>> Build()
    {
        FlushCurrent();

        foreach (var action in _actions)
        {
            if (string.IsNullOrWhiteSpace(action.ActionName))
                return Result<List<SequenceActionModel>>.Failure(
                    ErrorKind.Validation, "Sequence action name must not be empty.");

            if (action.ActionName.Length > 72)
                return Result<List<SequenceActionModel>>.Failure(
                    ErrorKind.Validation, $"Sequence action name '{action.ActionName}' exceeds 72 characters.");
        }

        return _actions;
    }

    private void FlushCurrent()
    {
        if (_currentActionName is null) return;

        var position = _currentPosition ?? new ActionPosition.AtNumber(4001);

        _actions.Add(new SequenceActionModel
        {
            ActionName = _currentActionName,
            Table = _table,
            Condition = _currentCondition,
            Position = position
        });

        _currentActionName = null;
        _currentPosition = null;
        _currentCondition = null;
    }
}
