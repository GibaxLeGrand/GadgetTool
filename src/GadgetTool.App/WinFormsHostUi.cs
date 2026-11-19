using GadgetTool.Core.Bridge;

namespace GadgetTool.App;

/// <summary>The file pickers AppBridge asks for, as Windows dialogs over the main window.</summary>
internal sealed class WinFormsHostUi(IWin32Window owner) : IHostUi
{
    private const string SetupsFilter = "GadgetTool setups (*.json)|*.json|All files (*.*)|*.*";

    public string? PickSaveToOpen()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open a save",
            Filter = "Saves (*.json;*.sav)|*.json;*.sav|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? PickExportPath(string suggestedName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export presets",
            Filter = SetupsFilter,
            DefaultExt = "json",
            FileName = suggestedName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? PickFileToImport()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import presets",
            Filter = SetupsFilter,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}
