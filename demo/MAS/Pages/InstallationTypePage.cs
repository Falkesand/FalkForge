using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class InstallationTypePage : MasPageBase<InstallationTypeView>
{
    private bool _isStandard = true;

    public override string Title => "Select installation type";

    public bool IsStandard
    {
        get => _isStandard;
        set
        {
            if (SetField(ref _isStandard, value))
                OnPropertyChanged(nameof(IsAdvanced));
        }
    }

    public bool IsAdvanced
    {
        get => !_isStandard;
        set => IsStandard = !value;
    }

    public override PageResult OnNext()
    {
        SharedState.Set("InstallationType", _isStandard ? "Standard" : "Advanced");
        return PageResult.GoTo<DatabaseServerPage>();
    }
}
