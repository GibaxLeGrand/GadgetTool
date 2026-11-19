namespace GadgetTool.App;

/// <summary>
/// Where things are. Options: <c>--catalog &lt;path&gt;</c> (another gadget list: a data pack's
/// <c>catalog.json</c>, whose <c>icons</c> folder is used too), <c>--save-key &lt;file&gt;</c>
/// (a file holding the game's save key: without it, only the made-up sample opens) and
/// <c>--dev</c> (the page's developer tools and context menu). <c>--data &lt;folder&gt;</c> (or a
/// <c>data</c> folder next to the exe holding an <c>index.json</c>) picks the data pack made for
/// the installed game; an explicit <c>--catalog</c> wins.
/// </summary>
internal sealed record AppOptions(string WebFolder, string CatalogPath, string? SampleSavePath, string DataFolder, bool Dev,
    string? SaveKeyPath = null, string? PacksFolder = null, bool CatalogGiven = false)
{
    /// <summary>A data pack keeps one icon per gadget next to its catalog: icons/&lt;id&gt;.png.</summary>
    public string IconsFolder => Path.Combine(Path.GetDirectoryName(CatalogPath) ?? ".", "icons");

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
        string? saveKey = null;
        string? packs = Directory.Exists(Path.Combine(baseDir, "data")) && File.Exists(Path.Combine(baseDir, "data", "index.json"))
            ? Path.Combine(baseDir, "data")
            : null;
        var catalogGiven = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--catalog" when i + 1 < args.Length:
                    catalog = Path.GetFullPath(args[++i]);
                    catalogGiven = true;
                    break;
                case "--data" when i + 1 < args.Length:
                    packs = Path.GetFullPath(args[++i]);
                    break;
                case "--save-key" when i + 1 < args.Length:
                    saveKey = Path.GetFullPath(args[++i]);
                    break;
                case "--dev":
                    dev = true;
                    break;
            }
        }

        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GadgetTool");
        return new AppOptions(Path.Combine(baseDir, "web"), catalog, Path.Combine(baseDir, "dev", "save.json"), data, dev, saveKey, packs, catalogGiven);
    }
}
