using System.Text.Json;
using System.Text.Json.Nodes;

namespace GadgetTool.Core.Gadgets;

/// <summary>One data pack: a game version, the Steam builds it was read from, its files and their SHA-256.</summary>
/// <param name="Folder">The pack's folder next to <c>index.json</c> (e.g. "2.0.0").</param>
/// <param name="Files">Path inside the folder ("catalog.json", "icons/x.png") → SHA-256, lower-case hex.</param>
public sealed record PackEntry(
    GameVersion GameVersion,
    IReadOnlyList<string> SteamBuildIds,
    string Folder,
    IReadOnlyDictionary<string, string> Files);

/// <summary>
/// <c>data/index.json</c>: the data packs a release (or a maintainer's folder) holds. The app
/// picks the pack made from the installed game's Steam build; without one, the newest pack.
/// See docs/DATA.md.
/// </summary>
public sealed class PackIndex
{
    public const string FormatName = "gadgettool.packs";
    public const int CurrentVersion = 1;

    public PackIndex(IEnumerable<PackEntry> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        Packs = [.. packs.OrderByDescending(p => p.GameVersion)];
        if (Packs.GroupBy(p => p.GameVersion).Any(g => g.Count() > 1))
            throw new ArgumentException("Two packs for the same game version.", nameof(packs));
    }

    public static PackIndex Empty { get; } = new([]);

    /// <summary>Newest game version first.</summary>
    public IReadOnlyList<PackEntry> Packs { get; }

    /// <summary>
    /// The pack for the installed game: the one read from its Steam build (<paramref name="exact"/>
    /// true), else the newest pack (false), else null when there is none.
    /// </summary>
    public PackEntry? Pick(string? steamBuildId, out bool exact)
    {
        var match = steamBuildId is null ? null : Packs.FirstOrDefault(p => p.SteamBuildIds.Contains(steamBuildId, StringComparer.Ordinal));
        exact = match is not null;
        return match ?? Packs.FirstOrDefault();
    }

    /// <summary>
    /// The index with <paramref name="pack"/> in it: a pack for the same game version is
    /// replaced, keeping the Steam builds it listed (a game update that changed no gadget data
    /// can share a pack).
    /// </summary>
    public PackIndex With(PackEntry pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var old = Packs.FirstOrDefault(p => p.GameVersion == pack.GameVersion);
        var builds = (old?.SteamBuildIds ?? []).Concat(pack.SteamBuildIds).Distinct(StringComparer.Ordinal).ToList();
        return new PackIndex(Packs.Where(p => p != old).Append(pack with { SteamBuildIds = builds }));
    }

    /// <exception cref="FormatException">Not an index this version can read.</exception>
    public static PackIndex Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Not valid JSON: {e.Message}", e);
        }
        if (node is not JsonObject root || (string?)root["format"] != FormatName)
            throw new FormatException($"Not a GadgetTool pack index (expected \"format\": \"{FormatName}\").");
        if (root["version"] is not JsonValue v || !v.TryGetValue(out int version) || version is < 1 or > CurrentVersion)
            throw new FormatException($"Unknown pack index version; this GadgetTool reads version {CurrentVersion}.");

        var packs = new List<PackEntry>();
        foreach (var item in root["packs"] as JsonArray ?? [])
        {
            if (item is not JsonObject p)
                throw new FormatException("A pack is not an object.");
            var gameVersion = GameVersion.Parse((string?)p["gameVersion"]);
            var folder = (string?)p["folder"];
            if (!gameVersion.IsKnown || string.IsNullOrWhiteSpace(folder) || folder.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(folder))
                throw new FormatException("A pack has no game version or a bad folder.");
            var builds = (p["steamBuildIds"] as JsonArray ?? []).Select(b => (string?)b).OfType<string>().ToList();
            var files = (p["files"] as JsonObject ?? []).ToDictionary(f => f.Key, f => (string?)f.Value ?? "", StringComparer.Ordinal);
            packs.Add(new PackEntry(gameVersion, builds, folder, files));
        }
        try
        {
            return new PackIndex(packs);
        }
        catch (ArgumentException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = CurrentVersion,
            ["packs"] = new JsonArray([.. Packs.Select(p => (JsonNode)new JsonObject
            {
                ["gameVersion"] = p.GameVersion.ToString(),
                ["steamBuildIds"] = new JsonArray([.. p.SteamBuildIds.Select(b => (JsonNode)b)]),
                ["folder"] = p.Folder,
                ["files"] = new JsonObject(p.Files.OrderBy(f => f.Key, StringComparer.Ordinal)
                    .Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))),
            })]),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
