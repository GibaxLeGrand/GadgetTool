using GadgetTool.Core.Bridge;
using GadgetTool.Core.Sharing;

namespace GadgetTool.App;

/// <summary>The file pickers AppBridge asks for, as Windows dialogs over the main window.</summary>
internal sealed class WinFormsHostUi(IWin32Window owner) : IHostUi
{
    private static readonly string Presets = $"*{SetupJson.FileExtension}";

    public string? PickSaveToOpen()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open a save",
            Filter = "Saves (*.sav;*.json)|*.sav;*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (GameSaveFolder() is { } folder)
            dialog.InitialDirectory = folder;
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>
    /// The game's save folder, %APPDATA%\SEGA\SonicRacingCrossWorlds\steam\&lt;Steam id&gt;, when
    /// there is exactly one Steam account there; else the folder above it, if it exists.
    /// </summary>
    private static string? GameSaveFolder()
    {
        var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SEGA", "SonicRacingCrossWorlds", "steam");
        if (!Directory.Exists(steam))
            return null;
        var accounts = Directory.GetDirectories(steam);
        return accounts.Length == 1 ? accounts[0] : steam;
    }

    public string? PickExportPath(string suggestedName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save presets to a file",
            Filter = $"GadgetTool presets ({Presets})|{Presets}",
            DefaultExt = SetupJson.FileExtension.TrimStart('.'),
            AddExtension = true,
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
            Filter = $"GadgetTool presets ({Presets};*.json)|{Presets};*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}
