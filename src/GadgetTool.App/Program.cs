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

        // A JSON gadget list: made-up by default, or a data pack's catalog.json (--catalog).
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
}
