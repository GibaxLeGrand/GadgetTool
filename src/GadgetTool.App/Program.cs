using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves.CrossWorlds;

namespace GadgetTool.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = AppOptions.Parse(args);

        // The data pack made for the installed game (by its Steam build), unless --catalog names one.
        if (!options.CatalogGiven && options.PacksFolder is { } packs)
        {
            try
            {
                var index = PackIndex.Parse(File.ReadAllText(Path.Combine(packs, "index.json")));
                var build = SteamInstall.FindBuildId(SteamRoot());
                var pack = index.Pick(build, out var exact)
                    ?? throw new FormatException("The data folder's index lists no pack.");
                options = options with { CatalogPath = Path.Combine(packs, pack.Folder, "catalog.json") };
                if (!exact)
                    MessageBox.Show(
                        $"No data pack was made from your game's Steam build ({build ?? "not found"}); GadgetTool uses the {pack.GameVersion} pack. " +
                        "If the game changed gadgets since, some may show wrong.",
                        "GadgetTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                MessageBox.Show($"The data packs could not be read from {packs}:\n\n{e.Message}",
                    "GadgetTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // A JSON gadget list: made-up by default, or a data pack's catalog.json.
        LocalizedCatalog catalog;
        try
        {
            catalog = CatalogJson.Parse(File.ReadAllText(options.CatalogPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            MessageBox.Show($"The gadget list could not be read from {options.CatalogPath}:\n\n{e.Message}",
                "GadgetTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // The game's save key: never in the repository; given by the user as a file.
        byte[]? saveKey = null;
        if (options.SaveKeyPath is not null)
        {
            try
            {
                saveKey = CrossWorldsSaveFormat.KeyFromText(File.ReadAllText(options.SaveKeyPath));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                MessageBox.Show($"The save key could not be read from {options.SaveKeyPath}:\n\n{e.Message}",
                    "GadgetTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        Application.Run(new MainForm(options, catalog, saveKey));
        return 0;
    }

    /// <summary>Steam's folder, as Steam records it for the user (read only), else its usual place.</summary>
    private static string SteamRoot()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path))
                return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }
}
