using FalkForge.Studio.Editors.DialogEditor;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class DialogEditorViewModelTests
{
    private static DialogEditorViewModel CreateVm(UiSection? model = null)
    {
        return new DialogEditorViewModel(model ?? new UiSection());
    }

    [Fact]
    public void AddDialog_AddsToCollection()
    {
        var vm = CreateVm();

        vm.AddDialogCommand.Execute(null);

        Assert.Single(vm.Dialogs);
        Assert.Equal("Dialog1", vm.Dialogs[0].Name);
    }

    [Fact]
    public void AddDialog_SetsSelected()
    {
        var vm = CreateVm();

        vm.AddDialogCommand.Execute(null);

        Assert.NotNull(vm.SelectedDialog);
        Assert.Equal("Dialog1", vm.SelectedDialog!.Name);
    }

    [Fact]
    public void AddDialog_UpdatesModel()
    {
        var model = new UiSection();
        var vm = CreateVm(model);

        vm.AddDialogCommand.Execute(null);

        Assert.Single(model.Dialogs);
    }

    [Fact]
    public void RemoveDialog_RemovesSelected()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.AddDialogCommand.Execute(null);
        vm.SelectedDialog = vm.Dialogs[0];

        vm.RemoveDialogCommand.Execute(null);

        Assert.Single(vm.Dialogs);
        Assert.Equal("Dialog2", vm.Dialogs[0].Name);
    }

    [Fact]
    public void RemoveDialog_UpdatesModel()
    {
        var model = new UiSection();
        var vm = CreateVm(model);
        vm.AddDialogCommand.Execute(null);
        vm.SelectedDialog = vm.Dialogs[0];

        vm.RemoveDialogCommand.Execute(null);

        Assert.Empty(model.Dialogs);
    }

    [Fact]
    public void RemoveDialog_SelectsFirst_WhenAvailable()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.AddDialogCommand.Execute(null);
        vm.SelectedDialog = vm.Dialogs[0];

        vm.RemoveDialogCommand.Execute(null);

        Assert.NotNull(vm.SelectedDialog);
    }

    [Fact]
    public void RemoveDialog_SelectsNull_WhenEmpty()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.SelectedDialog = vm.Dialogs[0];

        vm.RemoveDialogCommand.Execute(null);

        Assert.Null(vm.SelectedDialog);
    }

    [Fact]
    public void AddControl_AddsToSelectedDialog()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);

        vm.AddControlCommand.Execute(null);

        Assert.Single(vm.SelectedDialog!.Controls);
    }

    [Fact]
    public void AddControl_SetsSelectedControl()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);

        vm.AddControlCommand.Execute(null);

        Assert.NotNull(vm.SelectedControl);
    }

    [Fact]
    public void RemoveControl_RemovesFromDialog()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.AddControlCommand.Execute(null);

        vm.RemoveControlCommand.Execute(null);

        Assert.Empty(vm.SelectedDialog!.Controls);
    }

    [Fact]
    public void LoadTemplate_Minimal_PopulatesDialogs()
    {
        var vm = CreateVm();
        vm.SelectedTemplateName = "Minimal";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Equal(3, vm.Dialogs.Count);
        Assert.Equal("WelcomeDlg", vm.Dialogs[0].Name);
        Assert.Equal("ProgressDlg", vm.Dialogs[1].Name);
        Assert.Equal("ExitDlg", vm.Dialogs[2].Name);
    }

    [Fact]
    public void LoadTemplate_InstallDir_AddsInstallDirDialog()
    {
        var vm = CreateVm();
        vm.SelectedTemplateName = "InstallDir";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Equal(4, vm.Dialogs.Count);
        Assert.Contains(vm.Dialogs, d => d.Name == "InstallDirDlg");
    }

    [Fact]
    public void LoadTemplate_UpdatesModel()
    {
        var model = new UiSection();
        var vm = CreateVm(model);
        vm.SelectedTemplateName = "Minimal";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Equal(3, model.Dialogs.Count);
    }

    [Fact]
    public void LoadTemplate_ClearsPrevious()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.AddDialogCommand.Execute(null);
        vm.SelectedTemplateName = "Minimal";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Equal(3, vm.Dialogs.Count);
    }

    [Fact]
    public void LoadTemplate_SetsSelectedDialog()
    {
        var vm = CreateVm();
        vm.SelectedTemplateName = "Minimal";

        vm.LoadTemplateCommand.Execute(null);

        Assert.NotNull(vm.SelectedDialog);
        Assert.Equal("WelcomeDlg", vm.SelectedDialog!.Name);
    }

    [Fact]
    public void DialogPropertyChanges_PropagateToModel()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);

        vm.SelectedDialogName = "MyDialog";
        vm.SelectedDialogTitle = "My Title";
        vm.SelectedDialogWidth = 400;
        vm.SelectedDialogHeight = 300;

        Assert.Equal("MyDialog", vm.SelectedDialog!.Name);
        Assert.Equal("My Title", vm.SelectedDialog.Title);
        Assert.Equal(400, vm.SelectedDialog.Width);
        Assert.Equal(300, vm.SelectedDialog.Height);
    }

    [Fact]
    public void ControlPropertyChanges_PropagateToModel()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.AddControlCommand.Execute(null);

        vm.SelectedControlType = DialogControlType.PushButton;
        vm.SelectedControlX = 10;
        vm.SelectedControlY = 20;
        vm.SelectedControlWidth = 80;
        vm.SelectedControlHeight = 25;
        vm.SelectedControlText = "OK";
        vm.SelectedControlProperty = "MyProp";
        vm.SelectedControlCondition = "1=1";

        var control = vm.SelectedControl!;
        Assert.Equal(DialogControlType.PushButton, control.Type);
        Assert.Equal(10, control.X);
        Assert.Equal(20, control.Y);
        Assert.Equal(80, control.Width);
        Assert.Equal(25, control.Height);
        Assert.Equal("OK", control.Text);
        Assert.Equal("MyProp", control.Property);
        Assert.Equal("1=1", control.Condition);
    }

    [Fact]
    public void SelectedControl_ClearsWhenDialogChanges()
    {
        var vm = CreateVm();
        vm.AddDialogCommand.Execute(null);
        vm.AddControlCommand.Execute(null);
        Assert.NotNull(vm.SelectedControl);

        vm.AddDialogCommand.Execute(null); // adds new dialog and selects it

        Assert.Null(vm.SelectedControl);
    }

    [Fact]
    public void TemplateNames_ContainsAllTemplates()
    {
        var vm = CreateVm();

        Assert.Equal(5, vm.TemplateNames.Length);
        Assert.Contains("Minimal", vm.TemplateNames);
        Assert.Contains("InstallDir", vm.TemplateNames);
        Assert.Contains("FeatureTree", vm.TemplateNames);
        Assert.Contains("Mondo", vm.TemplateNames);
        Assert.Contains("Advanced", vm.TemplateNames);
    }

    [Fact]
    public void ControlTypes_ContainsAllEnumValues()
    {
        var vm = CreateVm();

        Assert.Equal(Enum.GetValues<DialogControlType>().Length, vm.ControlTypes.Length);
    }

    [Fact]
    public void LoadTemplate_FeatureTree_HasFeaturesDlg()
    {
        var vm = CreateVm();
        vm.SelectedTemplateName = "FeatureTree";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Contains(vm.Dialogs, d => d.Name == "FeaturesDlg");
    }

    [Fact]
    public void LoadTemplate_Advanced_HasSetupTypeDlg()
    {
        var vm = CreateVm();
        vm.SelectedTemplateName = "Advanced";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Contains(vm.Dialogs, d => d.Name == "SetupTypeDlg");
    }

    [Fact]
    public void InitialState_EmptyDialogs()
    {
        var vm = CreateVm();

        Assert.Empty(vm.Dialogs);
        Assert.Null(vm.SelectedDialog);
        Assert.Null(vm.SelectedControl);
    }

    [Fact]
    public void Constructor_LoadsExistingDialogsFromModel()
    {
        var model = new UiSection();
        model.Dialogs.Add(new DialogDefinition { Name = "Existing" });

        var vm = CreateVm(model);

        Assert.Single(vm.Dialogs);
        Assert.Equal("Existing", vm.Dialogs[0].Name);
    }
}
