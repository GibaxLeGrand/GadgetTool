using System.Text.Json.Nodes;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace GadgetTool.Extractor;

/// <summary>
/// The game's pak files, read with CUE4Parse: base game only (the <c>~mods</c> folder is
/// skipped), with the maintainer's AES key and mappings. Read only.
/// </summary>
internal sealed class GameFiles : IDisposable
{
    public const string GadgetTable = "UNION/Content/01_Union/Database/Gadget/DT_GadgetData";
    public const string CategoryTable = "UNION/Content/01_Union/Database/Gadget/DT_GadgetCustomCategoryData";
    private const string LocalizationFolder = "UNION/Content/Localization/Game/";

    private readonly DefaultFileProvider _provider;

    /// <param name="paksFolder">The game's <c>UNION\Content\Paks</c> folder.</param>
    /// <param name="aesKey">The pak AES key, "0x" and 64 hex digits.</param>
    /// <param name="mappings">A <c>.usmap</c> made for this game version.</param>
    /// <param name="oodle">The Oodle library (<c>oo2core_9_win64.dll</c>); never downloaded here.</param>
    public GameFiles(string paksFolder, string aesKey, string mappings, string oodle)
    {
        if (!Directory.Exists(paksFolder))
            throw new DirectoryNotFoundException($"No pak folder at '{paksFolder}'.");
        if (!File.Exists(mappings))
            throw new FileNotFoundException("The mappings file does not exist.", mappings);
        if (!File.Exists(oodle))
            throw new FileNotFoundException("The Oodle library does not exist.", oodle);

        OodleHelper.Initialize(oodle);
        _provider = new DefaultFileProvider(paksFolder, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE5_4))
        {
            MappingsContainer = new FileUsmapTypeMappingsProvider(mappings),
        };
        _provider.Initialize();
        _provider.SubmitKey(new FGuid(), new FAesKey(aesKey));
        _provider.PostMount();
        if (!_provider.Files.ContainsKey(GadgetTable + ".uasset"))
            throw new InvalidOperationException("The gadget table is not readable: wrong AES key, or not this game's pak folder.");
    }

    /// <summary>The languages the game ships texts for, as its localisation folders name them.</summary>
    public IReadOnlyList<string> Languages() =>
        [.. _provider.Files.Keys
            .Where(k => k.StartsWith(LocalizationFolder, StringComparison.OrdinalIgnoreCase) && k.EndsWith("/Game.locres", StringComparison.OrdinalIgnoreCase))
            .Select(k => k[LocalizationFolder.Length..^"/Game.locres".Length])
            .Where(l => l.Length > 0 && !l.Contains('/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>A DataTable's rows as CUE4Parse writes them in JSON, with texts in <paramref name="language"/>.</summary>
    public JsonObject TableRows(string tablePath, string language)
    {
        _provider.ChangeCulture(language);
        var exports = _provider.LoadPackage(tablePath).GetExports();
        var json = JsonNode.Parse(JsonConvert.SerializeObject(exports)) as JsonArray
            ?? throw new InvalidOperationException($"'{tablePath}' did not load.");
        return json.OfType<JsonObject>().Select(e => e["Rows"]).OfType<JsonObject>().FirstOrDefault()
            ?? throw new InvalidOperationException($"'{tablePath}' is not a DataTable.");
    }

    public void Dispose() => _provider.Dispose();
}
