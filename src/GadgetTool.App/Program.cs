using GadgetTool.Core.Gadgets;

namespace GadgetTool.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = AppOptions.Parse(args);

        // Until the catalog is read from the game's files: a JSON gadget list (made-up by default).
        GadgetCatalog catalog;
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

        Application.Run(new MainForm(options, catalog));
        return 0;
    }
}
