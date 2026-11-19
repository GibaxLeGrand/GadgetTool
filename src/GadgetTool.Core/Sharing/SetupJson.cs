using System.Text.Json;
using System.Text.Json.Serialization;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Sharing;

/// <summary>A preset in a presets file.</summary>
/// <param name="Name">Its name in GadgetTool (the game has no preset names), or null.</param>
/// <param name="Plate">Its gadgets.</param>
/// <param name="From">Where it was exported from, so a whole export can be put back in place; null if unknown.</param>
public sealed record SharedPlate(string? Name, GadgetPlate Plate, PlateRef? From = null);

/// <summary>
/// The presets file, to save on the PC and send to others (extension
/// <see cref="FileExtension"/>; plain JSON inside). Ids are not checked against a catalog
/// here: the paste does that, per player.
/// </summary>
/// <example>
/// <code>
/// { "format": "gadgettool.setups", "version": 1,
///   "plates": [ { "name": "Time trial", "player": 1, "preset": "A",
///                 "gadgets": ["..."], "super": "..." } ] }
/// </code>
/// </example>
public static class SetupJson
{
    public const string FormatName = "gadgettool.setups";
    public const int CurrentVersion = 1;
    public const string FileExtension = ".gtpresets";

    public static string Export(IEnumerable<SharedPlate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);

        var document = new SetupsDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            Plates = [.. plates.Select(p => new PlateEntry
            {
                Name = p.Name,
                Player = p.From?.Player,
                Preset = p.From is { } from ? PlayerSetups.PresetLetter(from.Index) : null,
                Gadgets = [.. p.Plate.GadgetIds],
                Super = p.Plate.SuperGadgetId,
            })],
        };
        return JsonSerializer.Serialize(document, SetupJsonContext.Default.SetupsDocument);
    }

    /// <exception cref="FormatException">The text is not a GadgetTool presets file this version can read.</exception>
    public static IReadOnlyList<SharedPlate> Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        SetupsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, SetupJsonContext.Default.SetupsDocument);
        }
        catch (JsonException e)
        {
            throw new FormatException($"Not valid JSON: {e.Message}", e);
        }

        if (document is null || document.Format != FormatName)
            throw new FormatException($"Not a GadgetTool presets file (expected \"format\": \"{FormatName}\").");
        if (document.Version > CurrentVersion)
            throw new FormatException($"The file uses version {document.Version}; this GadgetTool reads up to {CurrentVersion}. Update GadgetTool.");
        if (document.Version < 1)
            throw new FormatException($"Unknown file version {document.Version}.");
        if (document.Plates is null)
            throw new FormatException("The file has no \"plates\" list.");

        var result = new List<SharedPlate>(document.Plates.Count);
        for (var i = 0; i < document.Plates.Count; i++)
        {
            var entry = document.Plates[i];
            if (entry?.Gadgets is null)
                throw new FormatException($"Preset {i + 1} has no \"gadgets\" list.");
            if (entry.Gadgets.Any(string.IsNullOrWhiteSpace))
                throw new FormatException($"Preset {i + 1} has an empty gadget id.");

            result.Add(new SharedPlate(entry.Name, new GadgetPlate(entry.Gadgets, entry.Super), Origin(entry, i)));
        }
        return result;
    }

    private static PlateRef? Origin(PlateEntry entry, int i)
    {
        if (entry.Player is null && entry.Preset is null)
            return null;
        if (entry.Player is not (>= PlayerSetups.MinPlayer and <= PlayerSetups.MaxPlayer)
            || entry.Preset is not { Length: 1 } letter
            || letter[0] is < 'A' or > 'Z')
            throw new FormatException($"Preset {i + 1} has a bad origin: a player is 1 to 4 and a preset a letter.");
        return new PlateRef(entry.Player.Value, letter[0] - 'A');
    }

    internal sealed class SetupsDocument
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public List<PlateEntry?>? Plates { get; set; }
    }

    internal sealed class PlateEntry
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Player { get; set; }

        /// <summary>The preset's letter, A for the first.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Preset { get; set; }

        public List<string>? Gadgets { get; set; }

        /// <summary>The Super Slot gadget.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Super { get; set; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SetupJson.SetupsDocument))]
internal sealed partial class SetupJsonContext : JsonSerializerContext;
