using GadgetTool.Core.Bridge;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Saves.CrossWorlds;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GadgetTool.App;

/// <summary>
/// The window: web/ in WebView2, served under a virtual host name, and AppBridge answering the
/// page's messages. The page cannot leave that host or open other windows.
/// </summary>
internal sealed class MainForm : Form
{
    private const string PageHost = "gadgettool.example";

    // The data pack's icons, on a host of their own: the page shows them as images only.
    private const string IconHost = "icons.gadgettool.example";

    // Seen on 2026-11-19 (game 2.0.0): the launcher SonicRacingCrossWorlds.exe starts
    // UNION\Binaries\Win64\SonicRacingCrossWorldsSteam.exe and stays open while the game runs.
    private static readonly string[] GameProcesses = ["SonicRacingCrossWorldsSteam", "SonicRacingCrossWorlds"];

    private static readonly Color Background = Color.FromArgb(14, 17, 24);

    private readonly AppOptions _options;
    private readonly AppBridge _bridge;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    private bool _closeConfirmed;

    /// <param name="saveKey">The game's save key, or null: then only the made-up sample opens.</param>
    public MainForm(AppOptions options, LocalizedCatalog catalog, byte[]? saveKey)
    {
        _options = options;
        // Writing the game's save places gadgets in cells, from their slot costs (the same in every language).
        var gadgets = catalog.For(catalog.DefaultLanguage);
        ISaveFormat format = saveKey is null
            ? new DevJsonSaveFormat()
            : new AnySaveFormat(
                new CrossWorldsSaveFormat(saveKey, id => gadgets.TryGet(id, out var g) ? g.SlotCost : null),
                new DevJsonSaveFormat());
        var icons = Directory.Exists(options.IconsFolder)
            ? Directory.GetFiles(options.IconsFolder, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        _bridge = new AppBridge(new AppBridgeOptions(
            catalog,
            format,
            new WinFormsHostUi(this),
            new ProcessNameProbe(GameProcesses),
            TimeProvider.System,
            options.BackupFolder,
            options.SampleSavePath,
            options.SampleCopyPath,
            new PresetNameStore(options.NamesFile),
            new SeenGameStore(options.SeenGameFile))
        {
            IconUrl = id => icons.Contains(id) ? $"https://{IconHost}/{Uri.EscapeDataString(id)}.png" : null,
        });

        Text = "GadgetTool";
        BackColor = Background;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1100, 700);
        Controls.Add(_web);

        Load += async (_, _) => await StartAsync();
        FormClosing += OnFormClosing;
    }

    private async Task StartAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _options.WebViewDataFolder);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "GadgetTool needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11. Install it from Microsoft's website, then start GadgetTool again.",
                "GadgetTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _closeConfirmed = true;
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _options.Dev;
        core.Settings.AreDefaultContextMenusEnabled = _options.Dev;
        core.Settings.AreBrowserAcceleratorKeysEnabled = _options.Dev;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.SetVirtualHostNameToFolderMapping(PageHost, _options.WebFolder, CoreWebView2HostResourceAccessKind.Deny);
        if (Directory.Exists(_options.IconsFolder))
            core.SetVirtualHostNameToFolderMapping(IconHost, _options.IconsFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{PageHost}/", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        };

        // Answer after the event returns: the bridge may open a file dialog, which must not run
        // inside a WebView2 event handler.
        core.WebMessageReceived += (_, e) =>
        {
            var request = e.WebMessageAsJson;
            BeginInvoke(() => core.PostWebMessageAsJson(_bridge.Handle(request)));
        };

        core.Navigate($"https://{PageHost}/index.html");
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeConfirmed || _web.CoreWebView2 is null || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
            return;

        e.Cancel = true;
        var unsaved = 0;
        try
        {
            var answer = await _web.CoreWebView2.ExecuteScriptAsync("window.GTApp ? GTApp.unsavedCount() : 0");
            _ = int.TryParse(answer, out unsaved);
        }
        catch (InvalidOperationException)
        {
            // The page is gone: nothing to ask about.
        }

        if (unsaved > 0 && MessageBox.Show(this,
                $"{unsaved} preset{(unsaved == 1 ? " is" : "s are")} not saved. Quit anyway?",
                "GadgetTool", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _closeConfirmed = true;
        Close();
    }
}
