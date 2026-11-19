namespace GadgetTool.App;

/// <summary>
/// Where things are. Options: <c>--catalog &lt;path&gt;</c> (another gadget list, for local
/// tests) and <c>--dev</c> (the page's developer tools and context menu).
/// </summary>
internal sealed record AppOptions(string WebFolder, string CatalogPath, string? SampleSavePath, string DataFolder, bool Dev)
{
    /// <summary>Backups never go next to the save: its folder may be synced by Steam Cloud.</summary>
    public string BackupFolder => Path.Combine(DataFolder, "Backups");

    public string WebViewDataFolder => Path.Combine(DataFolder, "WebView2");

    public string SampleCopyPath => Path.Combine(DataFolder, "sample-save.json");

    /// <summary>The presets' names: GadgetTool only, never written into a save.</summary>
    public string NamesFile => Path.Combine(DataFolder, "preset-names.json");

    /// <summary>The game each save was last seen with, to report what an update changed.</summary>
    public string SeenGameFile => Path.Combine(DataFolder, "seen-game.json");

    public static AppOptions Parse(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var catalog = Path.Combine(baseDir, "dev", "catalog.json");
        var dev = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--catalog" when i + 1 < args.Length:
                    catalog = Path.GetFullPath(args[++i]);
                    break;
                case "--dev":
                    dev = true;
                    break;
            }
        }

        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GadgetTool");
        return new AppOptions(Path.Combine(baseDir, "web"), catalog, Path.Combine(baseDir, "dev", "save.json"), data, dev);
    }
}
