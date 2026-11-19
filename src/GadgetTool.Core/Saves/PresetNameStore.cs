using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves;

/// <summary>
/// Names for presets. The game has none: they live only in GadgetTool, in a file of their own
/// (never in the save), keyed by the save's full path and the preset.
/// </summary>
/// <example>
/// <code>
/// { "format": "gadgettool.names", "version": 1,
///   "saves": { "C:\\...\\save.sav": { "1:0": "Time trial", "2:3": "Grand Prix" } } }
/// </code>
/// </example>
public sealed class PresetNameStore
{
    public const int MaxLength = 40;
    private const string FormatName = "gadgettool.names";

    private readonly string _file;
    private readonly Dictionary<string, Dictionary<string, string>> _saves = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="file">The names file. Missing or unreadable means no names yet: names are a comfort, never a reason to fail.</param>
    public PresetNameStore(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        _file = file;
        Load();
    }

    /// <summary>Names of the presets of this save, by "player:index" (index 0 is preset A).</summary>
    public IReadOnlyDictionary<string, string> NamesFor(string savePath) =>
        _saves.TryGetValue(Key(savePath), out var names) ? new Dictionary<string, string>(names) : new Dictionary<string, string>();

    /// <summary>Names a preset, or removes its name when <paramref name="name"/> is blank. Returns the name as kept.</summary>
    public string? Set(string savePath, PlateRef preset, string? name)
    {
        var key = Key(savePath);
        var cleaned = Clean(name);
        var presetKey = $"{preset.Player}:{preset.Index}";

        if (cleaned is null)
        {
            if (_saves.TryGetValue(key, out var names) && names.Remove(presetKey) && names.Count == 0)
                _saves.Remove(key);
        }
        else
        {
            if (!_saves.TryGetValue(key, out var names))
                _saves[key] = names = new Dictionary<string, string>(StringComparer.Ordinal);
            names[presetKey] = cleaned;
        }

        Persist();
        return cleaned;
    }

    /// <summary>Trimmed, single-line, at most <see cref="MaxLength"/> characters; null when nothing is left.</summary>
    public static string? Clean(string? name)
    {
        if (name is null)
            return null;

        var text = new StringBuilder(name.Length);
        foreach (var c in name)
            text.Append(char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c);
        var single = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (single.Length > MaxLength)
            single = single[..MaxLength].TrimEnd();
        return single.Length == 0 ? null : single;
    }

    private static string Key(string savePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        return Path.GetFullPath(savePath);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file) || JsonNode.Parse(File.ReadAllText(_file)) is not JsonObject root
                || root["format"]?.GetValue<string>() != FormatName || root["saves"] is not JsonObject saves)
                return;

            foreach (var (path, value) in saves)
            {
                if (value is not JsonObject presets)
                    continue;
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (preset, name) in presets)
                {
                    if (name is JsonValue v && v.TryGetValue(out string? text) && Clean(text) is { } cleaned)
                        names[preset] = cleaned;
                }
                if (names.Count > 0)
                    _saves[path] = names;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            _saves.Clear();
        }
    }

    private void Persist()
    {
        var saves = new JsonObject();
        foreach (var (path, names) in _saves.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            var presets = new JsonObject();
            foreach (var (preset, name) in names.OrderBy(n => n.Key, StringComparer.Ordinal))
                presets[preset] = name;
            saves[path] = presets;
        }

        var root = new JsonObject { ["format"] = FormatName, ["version"] = 1, ["saves"] = saves };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _file, overwrite: true);
    }
}
