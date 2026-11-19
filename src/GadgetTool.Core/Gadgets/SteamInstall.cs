namespace GadgetTool.Core.Gadgets;

/// <summary>
/// Reads, never writes, what Steam knows of the installed game: which library holds it and
/// its build id. The build id picks the data pack that matches the game (see <see cref="PackIndex"/>).
/// </summary>
public static class SteamInstall
{
    /// <summary>Sonic Racing CrossWorlds on Steam.</summary>
    public const string AppId = "2486820";

    /// <summary>The installed build of <paramref name="appId"/>, or null when Steam or the game is not found.</summary>
    /// <param name="steamRoot">Steam's folder (the one holding <c>steamapps</c>).</param>
    public static string? FindBuildId(string steamRoot, string appId = AppId)
    {
        try
        {
            var folders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            var libraries = File.Exists(folders) ? LibrariesWith(appId, File.ReadAllText(folders)) : [];
            foreach (var library in libraries.Append(steamRoot))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
                if (File.Exists(manifest))
                    return BuildId(File.ReadAllText(manifest));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        return null;
    }

    /// <summary>The <c>buildid</c> of a Steam <c>appmanifest_*.acf</c>, or null.</summary>
    public static string? BuildId(string appManifest) => Values(appManifest, "buildid").FirstOrDefault(v => v.All(char.IsAsciiDigit) && v.Length > 0);

    /// <summary>
    /// The libraries of <c>libraryfolders.vdf</c> whose app list holds <paramref name="appId"/>:
    /// each library block gives its <c>path</c> before its <c>apps</c>.
    /// </summary>
    public static IReadOnlyList<string> LibrariesWith(string appId, string libraryFolders)
    {
        var found = new List<string>();
        string? path = null;
        foreach (var line in libraryFolders.Split('\n'))
        {
            var parts = Quoted(line);
            if (parts.Count == 2 && parts[0].Equals("path", StringComparison.OrdinalIgnoreCase))
                path = parts[1];
            else if (parts.Count == 2 && parts[0] == appId && path is not null && !found.Contains(path))
                found.Add(path);
        }
        return found;
    }

    private static IEnumerable<string> Values(string vdf, string key) =>
        vdf.Split('\n').Select(Quoted).Where(p => p.Count == 2 && p[0].Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => p[1]);

    /// <summary>The quoted strings of one VDF line ("key" "value"), with \\ and \" unescaped.</summary>
    private static List<string> Quoted(string line)
    {
        var parts = new List<string>();
        var current = (System.Text.StringBuilder?)null;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (current is null)
            {
                if (c == '"')
                    current = new System.Text.StringBuilder();
            }
            else if (c == '\\' && i + 1 < line.Length)
            {
                current.Append(line[++i]);
            }
            else if (c == '"')
            {
                parts.Add(current.ToString());
                current = null;
            }
            else
            {
                current.Append(c);
            }
        }
        return parts;
    }
}
