namespace GadgetTool.Core.Gadgets;

/// <summary>
/// Every gadget the game knows. In the app it is read from the game's own files; in tests it
/// is written by hand. Ids are unique, and so are their share-code hashes.
/// </summary>
public sealed class GadgetCatalog
{
    private readonly Dictionary<string, GadgetDefinition> _byId;
    private readonly Dictionary<uint, GadgetDefinition> _byShareHash;

    public GadgetCatalog(IEnumerable<GadgetDefinition> gadgets)
    {
        ArgumentNullException.ThrowIfNull(gadgets);

        var all = gadgets.ToList();
        _byId = new Dictionary<string, GadgetDefinition>(StringComparer.Ordinal);
        _byShareHash = [];

        foreach (var gadget in all)
        {
            if (string.IsNullOrWhiteSpace(gadget.Id))
                throw new ArgumentException("A gadget has an empty id.", nameof(gadgets));
            if (gadget.SlotCost < 1)
                throw new ArgumentException($"Gadget '{gadget.Id}' costs {gadget.SlotCost} slots; the minimum is 1.", nameof(gadgets));
            if (!_byId.TryAdd(gadget.Id, gadget))
                throw new ArgumentException($"Gadget id '{gadget.Id}' appears twice.", nameof(gadgets));

            var hash = ShareHash(gadget.Id);
            if (!_byShareHash.TryAdd(hash, gadget))
                throw new ArgumentException(
                    $"Gadgets '{_byShareHash[hash].Id}' and '{gadget.Id}' have the same share-code hash; share codes cannot tell them apart.",
                    nameof(gadgets));
        }

        Gadgets = all;
    }

    public IReadOnlyList<GadgetDefinition> Gadgets { get; }

    public bool TryGet(string id, out GadgetDefinition gadget) => _byId.TryGetValue(id, out gadget!);

    internal bool TryGetByShareHash(uint hash, out GadgetDefinition gadget) => _byShareHash.TryGetValue(hash, out gadget!);

    /// <summary>
    /// The 32-bit FNV-1a hash of the id's UTF-8 bytes. Share codes carry this instead of the
    /// id, which keeps them short and stable across game patches as long as ids do not change.
    /// </summary>
    internal static uint ShareHash(string id) => Fnv1a.Hash(System.Text.Encoding.UTF8.GetBytes(id));
}
