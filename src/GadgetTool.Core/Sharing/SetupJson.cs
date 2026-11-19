using System.Text.Json;
using System.Text.Json.Serialization;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Sharing;

/// <summary>A plate in an export file, with an optional label ("P1 plate 2", "Time trial"...).</summary>
public sealed record SharedPlate(string? Label, GadgetPlate Plate);

/// <summary>
/// Export and import of any number of plates as a JSON file, for backups and sharing whole
/// sets. Ids are not checked against a catalog here: the paste does that, per player.
/// </summary>
public static class SetupJson
{
    public const string FormatName = "gadgettool.setups";
    public const int CurrentVersion = 1;

    public static string Export(IEnumerable<SharedPlate> plates)
    {
        ArgumentNullException.ThrowIfNull(plates);

        var document = new SetupsDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            Plates = [.. plates.Select(p => new PlateEntry
            {
                Label = p.Label,
                Gadgets = [.. p.Plate.GadgetIds],
                Super = p.Plate.SuperGadgetId,
            })],
        };
        return JsonSerializer.Serialize(document, SetupJsonContext.Default.SetupsDocument);
    }

    /// <exception cref="FormatException">The text is not a GadgetTool setups file this version can read.</exception>
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
            throw new FormatException($"Not a GadgetTool setups file (expected \"format\": \"{FormatName}\").");
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
                throw new FormatException($"Plate {i + 1} has no \"gadgets\" list.");
            if (entry.Gadgets.Any(string.IsNullOrWhiteSpace))
                throw new FormatException($"Plate {i + 1} has an empty gadget id.");

            result.Add(new SharedPlate(entry.Label, new GadgetPlate(entry.Gadgets, entry.Super)));
        }
        return result;
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
        public string? Label { get; set; }

        public List<string>? Gadgets { get; set; }

        /// <summary>The Super Slot gadget.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Super { get; set; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SetupJson.SetupsDocument))]
internal sealed partial class SetupJsonContext : JsonSerializerContext;
