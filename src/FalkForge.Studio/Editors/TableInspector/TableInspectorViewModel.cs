using System.Collections.ObjectModel;
using System.Data;
using FalkForge.Studio.Inspect;

namespace FalkForge.Studio.Editors.TableInspector;

/// <summary>
/// ViewModel for the MSI Table Inspector. Opens an MSI file read-only and browses its internal tables.
/// </summary>
public sealed class TableInspectorViewModel : ViewModelBase
{
    private string _msiFilePath = string.Empty;
    private string? _selectedTableName;
    private DataView? _tableRows;
    private string _statusText = "Select an MSI file to inspect.";
    private bool _isLoading;

    public string MsiFilePath
    {
        get => _msiFilePath;
        set
        {
            if (SetProperty(ref _msiFilePath, value))
                OnMsiFileChanged();
        }
    }

    public ObservableCollection<string> TableNames { get; } = [];

    public string? SelectedTableName
    {
        get => _selectedTableName;
        set
        {
            if (SetProperty(ref _selectedTableName, value))
                OnSelectedTableChanged();
        }
    }

    public DataView? TableRows
    {
        get => _tableRows;
        private set => SetProperty(ref _tableRows, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Raised when the table data changes so the view can regenerate DataGrid columns.
    /// </summary>
    public event EventHandler<MsiTableData?>? TableDataChanged;

    private void OnMsiFileChanged()
    {
        TableNames.Clear();
        SelectedTableName = null;
        TableRows = null;

        if (string.IsNullOrWhiteSpace(_msiFilePath))
        {
            StatusText = "Select an MSI file to inspect.";
            return;
        }

        IsLoading = true;
        StatusText = "Loading table list...";

        var result = MsiTableReader.GetTableNames(_msiFilePath);
        IsLoading = false;

        if (result.IsFailure)
        {
            StatusText = result.Error.Message;
            return;
        }

        foreach (var name in result.Value)
            TableNames.Add(name);

        StatusText = $"{TableNames.Count} tables found.";
    }

    private void OnSelectedTableChanged()
    {
        if (_selectedTableName is null || string.IsNullOrWhiteSpace(_msiFilePath))
        {
            TableRows = null;
            TableDataChanged?.Invoke(this, null);
            return;
        }

        IsLoading = true;
        StatusText = $"Loading table '{_selectedTableName}'...";

        var result = MsiTableReader.ReadTable(_msiFilePath, _selectedTableName);
        IsLoading = false;

        if (result.IsFailure)
        {
            StatusText = result.Error.Message;
            TableRows = null;
            TableDataChanged?.Invoke(this, null);
            return;
        }

        var data = result.Value;
        var dt = new DataTable();
        foreach (var col in data.Columns)
            dt.Columns.Add(col, typeof(string));

        foreach (var row in data.Rows)
        {
            var dr = dt.NewRow();
            for (var i = 0; i < row.Count && i < data.Columns.Count; i++)
                dr[i] = row[i];
            dt.Rows.Add(dr);
        }

        TableRows = dt.DefaultView;
        StatusText = $"Table '{_selectedTableName}': {data.Rows.Count} rows, {data.Columns.Count} columns.";
        TableDataChanged?.Invoke(this, data);
    }

    /// <summary>
    /// Loads table names for the given MSI path. Called from code-behind after file dialog.
    /// </summary>
    internal void LoadMsiFile(string path)
    {
        MsiFilePath = path;
    }
}
