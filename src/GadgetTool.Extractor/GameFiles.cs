using System.Text.Json.Nodes;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;
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
    public const string MiniGadgetList = "UNION/Content/01_Union/Database/Gadget/DA_MiniGadgetList";
    public const string PlateTable = "UNION/Content/01_Union/Database/Gadget/DT_GadgetPlateData";
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

    /// <summary>The gadget ids of <c>DA_MiniGadgetList</c> ("EGadgetId::..."), which get the Mini icon base.</summary>
    public IReadOnlyList<string> MiniGadgets()
    {
        var exports = _provider.LoadPackage(MiniGadgetList).GetExports();
        var json = JsonNode.Parse(JsonConvert.SerializeObject(exports)) as JsonArray;
        return [.. (json?.OfType<JsonObject>().Select(e => e["Properties"]?["MiniGadgetList"]).OfType<JsonArray>().FirstOrDefault() ?? [])
            .Select(v => (string?)v).OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// A texture's largest mip as 8-bit RGBA, rows from the top. BC7 (the game's UI icons), BC3,
    /// BC1 and uncompressed BGRA are decoded; anything else is refused.
    /// </summary>
    /// <param name="packagePath">The texture's package, e.g. <c>UNION/Content/.../UI_Icon</c>.</param>
    public (int Width, int Height, byte[] Rgba) TextureRgba(string packagePath)
    {
        var texture = _provider.LoadPackage(ActualPath(packagePath)).GetExports().OfType<UTexture2D>().FirstOrDefault()
            ?? throw new InvalidOperationException($"'{packagePath}' is not a texture.");
        var mip = texture.GetFirstMip()
            ?? throw new InvalidOperationException($"'{packagePath}' has no pixel data.");
        var data = mip.BulkData?.Data
            ?? throw new InvalidOperationException($"'{packagePath}' has no pixel data.");
        int width = mip.SizeX, height = mip.SizeY;

        byte[] rgba;
        switch (texture.Format)
        {
            case EPixelFormat.PF_BC7:
                Bc7.Decompress<ColorRGBA<byte>, byte>(data, width, height, out rgba);
                break;
            case EPixelFormat.PF_DXT5:
                Bc3.Decompress<ColorRGBA<byte>, byte>(data, width, height, out rgba);
                break;
            case EPixelFormat.PF_DXT1:
                Bc1.Decompress<ColorRGBA<byte>, byte>(data, width, height, out rgba);
                break;
            case EPixelFormat.PF_B8G8R8A8:
                rgba = new byte[width * height * 4];
                for (var i = 0; i + 3 < rgba.Length && i + 3 < data.Length; i += 4)
                {
                    rgba[i] = data[i + 2];
                    rgba[i + 1] = data[i + 1];
                    rgba[i + 2] = data[i];
                    rgba[i + 3] = data[i + 3];
                }
                break;
            default:
                throw new InvalidOperationException($"'{packagePath}' is in {texture.Format}, which the extractor does not decode.");
        }
        return (width, height, rgba);
    }

    /// <summary>
    /// The package's path as the paks spell it. The game's tables do not always use the same
    /// letter case as the files (an icon is named "..._run60" in the table, "..._Run60" in the
    /// paks); Unreal ignores case, so this does too.
    /// </summary>
    private string ActualPath(string packagePath)
    {
        if (_provider.Files.ContainsKey(packagePath + ".uasset"))
            return packagePath;
        var match = _provider.Files.Keys.FirstOrDefault(k => k.Equals(packagePath + ".uasset", StringComparison.OrdinalIgnoreCase));
        return match is null ? packagePath : match[..^".uasset".Length];
    }

    public void Dispose() => _provider.Dispose();
}
