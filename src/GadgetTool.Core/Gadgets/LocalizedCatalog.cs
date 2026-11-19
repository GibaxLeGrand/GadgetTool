namespace GadgetTool.Core.Gadgets;

/// <summary>
/// One text in several languages, keyed by language code as the game's localisation names
/// them ("en", "fr", "ja", "zh-Hans"...).
/// </summary>
public sealed class LocalizedText
{
    public static LocalizedText Empty { get; } = new(new Dictionary<string, string>());

    public LocalizedText(IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        Texts = texts
            .Where(t => !string.IsNullOrWhiteSpace(t.Key) && !string.IsNullOrEmpty(t.Value))
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Texts { get; }

    /// <summary>
    /// The text in <paramref name="language"/>, else in its base language ("fr-CA" → "fr"),
    /// else in <paramref name="fallback"/>, else in any language; null when there is none.
    /// </summary>
    public string? Get(string language, string fallback)
    {
        if (Texts.TryGetValue(language, out var text))
            return text;
        var dash = language.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && Texts.TryGetValue(language[..dash], out text))
            return text;
        if (Texts.TryGetValue(fallback, out text))
            return text;
        return Texts.Values.FirstOrDefault();
    }
}

/// <summary>A gadget with its texts in every language the data has.</summary>
/// <param name="CategoryId">A key for the category that does not change with the language (grouping, colours).</param>
/// <param name="SuperEffect">What it does in the Super Slot; null or empty when it has no Super effect.</param>
/// <param name="Base">The frame the game draws the icon on: "kit", "mini", or null for the usual one.</param>
public sealed record LocalizedGadget(
    string Id,
    int SlotCost,
    LocalizedText Name,
    string CategoryId,
    LocalizedText Category,
    LocalizedText Description,
    LocalizedText? SuperEffect = null,
    string? Base = null);

/// <summary>
/// The gadget list with the game's texts in all its languages, as the extractor writes it.
/// <see cref="For"/> gives the plain catalog in one language; ids, slot costs and the game
/// version are the same in every language, so rules, share codes and files do not depend on it.
/// </summary>
public sealed class LocalizedCatalog
{
    private readonly IReadOnlyList<LocalizedGadget> _gadgets;
    private readonly Dictionary<string, GadgetCatalog> _byLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="categoryColors">The game's colour per category id ("#RRGGBB"), when the data has them.</param>
    /// <exception cref="ArgumentException">An id is empty or repeated, or a slot cost is below 1.</exception>
    public LocalizedCatalog(IEnumerable<LocalizedGadget> gadgets, string source, GameVersion gameVersion, string defaultLanguage,
        IReadOnlyDictionary<string, string>? categoryColors = null)
    {
        CategoryColors = categoryColors ?? new Dictionary<string, string>();
        ArgumentNullException.ThrowIfNull(gadgets);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultLanguage);

        _gadgets = [.. gadgets];
        Source = source ?? "";
        GameVersion = gameVersion;
        DefaultLanguage = defaultLanguage;

        var others = _gadgets
            .SelectMany(g => g.Name.Texts.Keys)
            .Where(l => !l.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        Languages = [defaultLanguage, .. others];

        _ = For(defaultLanguage); // checks ids and costs once
    }

    /// <summary>A catalog with texts in one language only (tests, the made-up and wiki lists).</summary>
    public static LocalizedCatalog FromSingle(GadgetCatalog catalog, string language = "en")
    {
        ArgumentNullException.ThrowIfNull(catalog);
        LocalizedText One(string? text) =>
            text is null ? LocalizedText.Empty : new LocalizedText(new Dictionary<string, string> { [language] = text });
        return new LocalizedCatalog(
            catalog.Gadgets.Select(g => new LocalizedGadget(g.Id, g.SlotCost, One(g.Name), g.CategoryKey, One(g.Category), One(g.Description))),
            catalog.Source, catalog.GameVersion, language);
    }

    public string Source { get; }

    public GameVersion GameVersion { get; }

    public string DefaultLanguage { get; }

    /// <summary>The game's colour per category id ("#RRGGBB"); empty when the data has none.</summary>
    public IReadOnlyDictionary<string, string> CategoryColors { get; }

    /// <summary>Every language the gadget names exist in, the default first.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>
    /// The language to use for a request: the same one, else its base language ("fr-FR" →
    /// "fr"), else the default.
    /// </summary>
    public string Resolve(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return DefaultLanguage;
        var exact = Languages.FirstOrDefault(l => l.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;
        var dash = requested.IndexOf('-', StringComparison.Ordinal);
        var baseLanguage = dash > 0 ? requested[..dash] : requested;
        return Languages.FirstOrDefault(l => l.Equals(baseLanguage, StringComparison.OrdinalIgnoreCase)
                                             || l.StartsWith(baseLanguage + "-", StringComparison.OrdinalIgnoreCase))
               ?? DefaultLanguage;
    }

    /// <summary>The catalog with every text in <paramref name="language"/> (missing texts fall back to the default language).</summary>
    public GadgetCatalog For(string? language)
    {
        var lang = Resolve(language);
        if (_byLanguage.TryGetValue(lang, out var cached))
            return cached;

        var catalog = new GadgetCatalog(
            _gadgets.Select(g => new GadgetDefinition(
                g.Id,
                g.Name.Get(lang, DefaultLanguage) ?? g.Id,
                g.SlotCost,
                g.Category.Get(lang, DefaultLanguage) ?? g.CategoryId,
                g.Description.Get(lang, DefaultLanguage),
                g.CategoryId,
                g.SuperEffect?.Get(lang, DefaultLanguage),
                CategoryColors.GetValueOrDefault(g.CategoryId),
                g.Base)),
            Source, GameVersion);
        _byLanguage[lang] = catalog;
        return catalog;
    }
}
