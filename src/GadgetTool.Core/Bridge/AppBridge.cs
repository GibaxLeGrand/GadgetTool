using System.Text.Json;
using System.Text.Json.Nodes;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Bridge;

/// <summary>What the bridge asks of the window: file pickers. Null means the user cancelled.</summary>
public interface IHostUi
{
    string? PickSaveToOpen();

    string? PickExportPath(string suggestedName);

    string? PickFileToImport();
}

/// <param name="Catalog">Every gadget the game knows.</param>
/// <param name="Format">Reads and writes saves.</param>
/// <param name="Ui">File pickers.</param>
/// <param name="Game">Tells whether the game runs (saving is refused while it does).</param>
/// <param name="Time">For backup names.</param>
/// <param name="BackupDirectory">
/// Where backups go. Never next to the save: the save folder may be synced by Steam Cloud.
/// </param>
/// <param name="SampleSavePath">A made-up save to try the tool with, or null.</param>
/// <param name="SampleCopyPath">Where the sample is copied before it is opened, so the original stays clean.</param>
/// <param name="Names">The presets' names (GadgetTool only, never in the save).</param>
public sealed record AppBridgeOptions(
    GadgetCatalog Catalog,
    ISaveFormat Format,
    IHostUi Ui,
    IGameProcessProbe Game,
    TimeProvider Time,
    string BackupDirectory,
    string? SampleSavePath,
    string SampleCopyPath,
    PresetNameStore Names);

/// <summary>
/// Answers the web page's calls. The page posts <c>{ "id", "method", "params" }</c>; the answer
/// is <c>{ "id", "result" }</c> or <c>{ "id", "error": { "kind", "message" } }</c>. The page
/// keeps a copy of the rules for instant feedback; this side is the one that reads and writes
/// the save, and it checks every rule again before writing.
/// </summary>
public sealed class AppBridge(AppBridgeOptions options)
{
    /// <summary>Import files larger than this are refused (setups files are a few kilobytes).</summary>
    public const long MaxImportBytes = 1024 * 1024;

    private readonly SafeSaveWriter _writer = new(options.Game, options.Time);
    private SaveSession? _session;

    public string Handle(string requestJson)
    {
        JsonNode? id = null;
        try
        {
            var request = Parse(requestJson);
            id = request["id"]?.DeepClone();
            var method = Text(request, "method") ?? throw new BridgeError("badRequest", "The request has no method.");
            var args = request["params"] as JsonObject ?? [];

            JsonNode result = method switch
            {
                "catalog" => Catalog(),
                "openSave" => OpenSave(Text(args, "path")),
                "openSample" => OpenSample(),
                "reloadSave" => OpenSave(_session?.Path ?? throw new BridgeError("noSave", "No save is open.")),
                "save" => Save(args),
                "renamePreset" => RenamePreset(args),
                "gameRunning" => new JsonObject { ["running"] = options.Game.IsGameRunning() },
                "exportFile" => ExportFile(args),
                "importFile" => ImportFile(),
                _ => throw new BridgeError("unknownMethod", $"Unknown method '{method}'."),
            };
            return new JsonObject { ["id"] = id, ["result"] = result }.ToJsonString();
        }
        catch (BridgeError e) { return Error(id, e.Kind, e.Message); }
        catch (GameRunningException e) { return Error(id, "gameRunning", e.Message); }
        catch (SaveChangedException e) { return Error(id, "saveChanged", e.Message); }
        catch (SaveFormatException e) { return Error(id, "saveFormat", e.Message); }
        catch (InvalidOperationException e) { return Error(id, "invalid", e.Message); }
        catch (ArgumentException e) { return Error(id, "badRequest", e.Message); }
        catch (IOException e) { return Error(id, "io", e.Message); }
        catch (UnauthorizedAccessException e) { return Error(id, "io", e.Message); }
    }

    private JsonObject Catalog() => new()
    {
        ["source"] = options.Catalog.Source,
        ["gadgets"] = new JsonArray([.. options.Catalog.Gadgets.Select(g => (JsonNode)new JsonObject
        {
            ["id"] = g.Id,
            ["name"] = g.Name,
            ["slotCost"] = g.SlotCost,
            ["category"] = g.Category,
            ["description"] = g.Description,
        })]),
    };

    private JsonObject OpenSave(string? path)
    {
        path ??= options.Ui.PickSaveToOpen();
        if (path is null)
            return Cancelled();

        _session = SaveSession.Open(path, options.Format, options.Catalog);
        return SaveDto(_session);
    }

    private JsonObject OpenSample()
    {
        if (options.SampleSavePath is null || !File.Exists(options.SampleSavePath))
            throw new BridgeError("noSample", "No sample save comes with this build.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.SampleCopyPath))!);
        File.Copy(options.SampleSavePath, options.SampleCopyPath, overwrite: true);
        return OpenSave(options.SampleCopyPath);
    }

    private JsonObject Save(JsonObject args)
    {
        var session = _session ?? throw new BridgeError("noSave", "No save is open.");
        var edited = session.Saved;

        foreach (var node in args["players"] as JsonArray ?? throw new BridgeError("badRequest", "No players to save."))
        {
            var player = node as JsonObject ?? throw new BridgeError("badRequest", "A player is not an object.");
            var number = player["number"] is JsonValue n && n.TryGetValue(out int value) ? value : throw new BridgeError("badRequest", "A player has no number.");
            var plates = player["plates"] as JsonArray ?? throw new BridgeError("badRequest", $"Player {number} has no plates.");
            if (plates.Count != session.Saved.Player(number).Plates.Count)
                throw new BridgeError("badRequest", $"Player {number} has {session.Saved.Player(number).Plates.Count} presets, not {plates.Count}.");

            for (var i = 0; i < plates.Count; i++)
                edited = edited.WithPlate(new PlateRef(number, i), ReadPlate(plates[i], $"P{number} preset {i + 1}"));
        }

        session.Update(edited);
        var changed = session.ChangedPlates.Count();
        var backup = session.Save(_writer, options.BackupDirectory);
        return new JsonObject { ["saved"] = changed, ["backup"] = backup };
    }

    private JsonObject RenamePreset(JsonObject args)
    {
        var session = _session ?? throw new BridgeError("noSave", "No save is open.");
        var preset = new PlateRef(Int(args, "player"), Int(args, "index"));
        if (!session.Saved.Contains(preset))
            throw new BridgeError("badRequest", $"The save has no {preset}.");

        return new JsonObject { ["name"] = options.Names.Set(session.Path, preset, Text(args, "name")) };
    }

    private JsonObject ExportFile(JsonObject args)
    {
        var content = Text(args, "content") ?? throw new BridgeError("badRequest", "Nothing to export.");
        var path = options.Ui.PickExportPath(Text(args, "suggestedName") ?? "gadgettool.json");
        if (path is null)
            return Cancelled();

        File.WriteAllText(path, content);
        return new JsonObject { ["path"] = path, ["fileName"] = Path.GetFileName(path) };
    }

    private JsonObject ImportFile()
    {
        var path = options.Ui.PickFileToImport();
        if (path is null)
            return Cancelled();
        if (new FileInfo(path).Length > MaxImportBytes)
            throw new BridgeError("tooLarge", "This file is too large to be a GadgetTool export.");

        return new JsonObject { ["content"] = File.ReadAllText(path), ["fileName"] = Path.GetFileName(path) };
    }

    private JsonObject SaveDto(SaveSession session) => new()
    {
        ["path"] = session.Path,
        ["names"] = new JsonObject([.. options.Names.NamesFor(session.Path)
            .Select(n => KeyValuePair.Create(n.Key, (JsonNode?)n.Value))]),
        ["fileName"] = Path.GetFileName(session.Path),
        ["version"] = session.Current.SaveVersion,
        ["players"] = new JsonArray([.. session.Current.Players.Select(p => (JsonNode)new JsonObject
        {
            ["number"] = p.PlayerNumber,
            ["level"] = p.Layout.LevelName,
            ["rows"] = new JsonArray([.. p.Layout.RowSizes.Select(r => (JsonNode)r)]),
            ["superSlot"] = p.Layout.HasSuperSlot,
            ["unlocked"] = new JsonArray([.. p.Unlocked.Order(StringComparer.Ordinal).Select(id => (JsonNode)id)]),
            ["plates"] = new JsonArray([.. p.Plates.Select(PlateDto)]),
        })]),
    };

    private static JsonNode PlateDto(GadgetPlate plate)
    {
        var dto = new JsonObject { ["gadgets"] = new JsonArray([.. plate.GadgetIds.Select(id => (JsonNode)id)]) };
        if (plate.SuperGadgetId is not null)
            dto["super"] = plate.SuperGadgetId;
        return dto;
    }

    private static GadgetPlate ReadPlate(JsonNode? node, string what)
    {
        var plate = node as JsonObject ?? throw new BridgeError("badRequest", $"{what} is not an object.");
        var ids = (plate["gadgets"] as JsonArray ?? throw new BridgeError("badRequest", $"{what} has no gadgets list."))
            .Select(g => g is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : throw new BridgeError("badRequest", $"{what} has a bad gadget id."))
            .ToList();
        return new GadgetPlate(ids, Text(plate, "super"));
    }

    private static JsonObject Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? throw new BridgeError("badRequest", "The request is not an object.");
        }
        catch (JsonException e)
        {
            throw new BridgeError("badRequest", $"The request is not valid JSON: {e.Message}");
        }
    }

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static int Int(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue(out int i) ? i : throw new BridgeError("badRequest", $"'{name}' is missing or not a number.");

    private static JsonObject Cancelled() => new() { ["cancelled"] = true };

    private static string Error(JsonNode? id, string kind, string message) =>
        new JsonObject { ["id"] = id, ["error"] = new JsonObject { ["kind"] = kind, ["message"] = message } }.ToJsonString();

    private sealed class BridgeError(string kind, string message) : Exception(message)
    {
        public string Kind { get; } = kind;
    }
}
