using System.Text.Json;
using System.Text.Json.Nodes;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves;

/// <summary>
/// A made-up save format in JSON, used to build and test the tool until the real one is known.
/// Like a real save, it holds things the tool must not touch (here <c>progress</c>, and any
/// extra field anywhere), and <see cref="Write"/> keeps them.
/// </summary>
/// <example>
/// <code>
/// {
///   "devSave": "gadgettool",
///   "version": "dev-1",
///   "players": [
///     { "player": 1, "rows": [3, 3], "superSlot": true, "unlocked": ["a", "b"],
///       "plates": [ { "gadgets": ["a"], "super": "b" }, { "gadgets": [] } ] }
///   ],
///   "progress": { "races": 120 }
/// }
/// </code>
/// </example>
public sealed class DevJsonSaveFormat : ISaveFormat
{
    public const string Marker = "gadgettool";
    public static IReadOnlySet<string> KnownVersions { get; } = new HashSet<string>(StringComparer.Ordinal) { "dev-1" };

    public string Name => "GadgetTool dev save (JSON)";

    public bool CanRead(ReadOnlySpan<byte> content)
    {
        try
        {
            return JsonNode.Parse(content) is JsonObject root
                && root["devSave"] is JsonValue marker
                && marker.TryGetValue(out string? value)
                && value == Marker;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public SaveSetups Read(ReadOnlySpan<byte> content)
    {
        var root = ParseRoot(content, out var version);
        var players = new List<PlayerSetups>();

        foreach (var node in PlayersArray(root))
        {
            var player = node as JsonObject ?? throw Damaged("a player is not an object");
            var number = Int(player, "player");
            if (number is < PlayerSetups.MinPlayer or > PlayerSetups.MaxPlayer)
                throw Damaged($"player number {number} is out of 1-4");
            if (players.Any(p => p.PlayerNumber == number))
                throw Damaged($"player {number} appears twice");

            var rows = (player["rows"] as JsonArray ?? throw Damaged($"player {number} has no rows"))
                .Select(r => r is JsonValue v && v.TryGetValue(out int size) ? size : throw Damaged($"player {number} has a bad row size"))
                .ToList();
            PlateLayout layout;
            try
            {
                layout = new PlateLayout(rows, Bool(player, "superSlot"));
            }
            catch (ArgumentException e)
            {
                throw Damaged($"player {number}: {e.Message}");
            }

            var unlocked = Strings(player["unlocked"], $"player {number} unlocked list").ToHashSet(GadgetIds.Comparer);
            var plates = (player["plates"] as JsonArray ?? throw Damaged($"player {number} has no plates"))
                .Select((p, i) => ReadPlate(p, $"player {number} plate {i + 1}"))
                .ToList();

            players.Add(new PlayerSetups(number, layout, unlocked, plates));
        }

        return new SaveSetups(version, players);
    }

    public byte[] Write(ReadOnlySpan<byte> original, SaveSetups setups)
    {
        ArgumentNullException.ThrowIfNull(setups);

        var root = ParseRoot(original, out var version);
        if (setups.SaveVersion != version)
            throw new SaveFormatException($"The setups come from a version {setups.SaveVersion} save; this save is version {version}.");

        var playerNodes = PlayersArray(root).OfType<JsonObject>().ToList();
        foreach (var player in setups.Players)
        {
            var node = playerNodes.FirstOrDefault(p => p["player"] is JsonValue v && v.TryGetValue(out int n) && n == player.PlayerNumber)
                ?? throw new SaveFormatException($"The save has no player {player.PlayerNumber}.");
            var plateNodes = node["plates"] as JsonArray ?? throw Damaged($"player {player.PlayerNumber} has no plates");
            if (plateNodes.Count != player.Plates.Count)
                throw new SaveFormatException($"Player {player.PlayerNumber} has {plateNodes.Count} plates in the save, not {player.Plates.Count}.");

            for (var i = 0; i < plateNodes.Count; i++)
            {
                // Only the gadget fields change; any other field of the plate stays.
                var plateNode = plateNodes[i] as JsonObject ?? throw Damaged($"player {player.PlayerNumber} plate {i + 1} is not an object");
                var plate = player.Plates[i];
                plateNode["gadgets"] = new JsonArray([.. plate.GadgetIds.Select(id => (JsonNode)JsonValue.Create(id))]);
                if (plate.SuperGadgetId is null)
                    plateNode.Remove("super");
                else
                    plateNode["super"] = plate.SuperGadgetId;
            }
        }

        return JsonSerializer.SerializeToUtf8Bytes(root, DevJsonContext.Default.JsonNode);
    }

    private static JsonObject ParseRoot(ReadOnlySpan<byte> content, out string version)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content);
        }
        catch (JsonException e)
        {
            throw new SaveFormatException($"The save is damaged: {e.Message}", e);
        }

        if (node is not JsonObject root
            || root["devSave"] is not JsonValue marker
            || !marker.TryGetValue(out string? markerValue)
            || markerValue != Marker)
            throw new SaveFormatException("Not a GadgetTool dev save.");

        version = root["version"] is JsonValue v && v.TryGetValue(out string? s) ? s : "";
        if (!KnownVersions.Contains(version))
            throw new SaveFormatException($"Save version '{version}' is unknown to this GadgetTool; it will not read or write it.");
        return root;
    }

    private static JsonArray PlayersArray(JsonObject root) => root["players"] as JsonArray ?? throw Damaged("no players list");

    private static GadgetPlate ReadPlate(JsonNode? node, string what)
    {
        var plate = node as JsonObject ?? throw Damaged($"{what} is not an object");
        var super = plate["super"] switch
        {
            null => null,
            JsonValue v when v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) => s,
            _ => throw Damaged($"{what} has a bad Super Slot gadget"),
        };
        return new GadgetPlate(Strings(plate["gadgets"], $"{what} gadget list"), super);
    }

    private static List<string> Strings(JsonNode? node, string what) =>
        (node as JsonArray ?? throw Damaged($"{what} is missing"))
            .Select(n => n is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : throw Damaged($"{what} has a bad entry"))
            .ToList();

    private static int Int(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out int value) ? value : throw Damaged($"'{name}' is missing or not a number");

    private static bool Bool(JsonObject obj, string name) =>
        obj[name] is null ? false
        : obj[name] is JsonValue v && v.TryGetValue(out bool value) ? value
        : throw Damaged($"'{name}' is not true or false");

    private static SaveFormatException Damaged(string why) => new($"The save is damaged: {why}.");
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonNode))]
internal sealed partial class DevJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
