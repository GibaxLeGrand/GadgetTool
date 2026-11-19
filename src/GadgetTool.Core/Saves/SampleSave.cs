using System.Text.Json;
using System.Text.Json.Nodes;
using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Rules;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Saves;

/// <summary>
/// A made-up save (in <see cref="DevJsonSaveFormat"/>) built from a gadget list, so the sample
/// a release ships shows the real gadgets: four players on four plate levels, every gadget
/// unlocked (Gibax), six presets filled with plates that follow every rule, four left empty.
/// The same list always gives the same save. The app keeps it read-only (<c>AppBridge</c>).
/// </summary>
public static class SampleSave
{
    private const int Presets = 10, Filled = 6;

    private static readonly PlateLayout[] Players =
    [
        PlateLayout.GrandMaster,
        PlateLayout.Master,
        new([3, 2], hasSuperSlot: false),
        new([3], hasSuperSlot: false),
    ];

    public static string Generate(GadgetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var players = new JsonArray();
        for (var p = 0; p < Players.Length; p++)
        {
            var layout = Players[p];
            var unlocked = catalog.Gadgets.Select(g => g.Id).ToList();
            var player = new PlayerSetups(p + 1, layout, unlocked.ToHashSet(GadgetIds.Comparer), []);

            var plates = new JsonArray();
            for (var i = 0; i < Presets; i++)
            {
                var plate = i < Filled ? Fill(catalog, player, new Random(2000 + 31 * p + i)) : GadgetPlate.Empty;
                var json = new JsonObject { ["gadgets"] = new JsonArray([.. plate.GadgetIds.Select(id => (JsonNode)id)]) };
                if (plate.SuperGadgetId is not null)
                    json["super"] = plate.SuperGadgetId;
                plates.Add(json);
            }

            players.Add(new JsonObject
            {
                ["player"] = p + 1,
                ["rows"] = new JsonArray([.. layout.RowSizes.Select(r => (JsonNode)r)]),
                ["superSlot"] = layout.HasSuperSlot,
                ["unlocked"] = new JsonArray([.. unlocked.Select(id => (JsonNode)id)]),
                ["plates"] = plates,
            });
        }

        var root = new JsonObject
        {
            ["devSave"] = DevJsonSaveFormat.Marker,
            ["version"] = "dev-1",
            ["note"] = "A made-up save generated from the gadget list, to try GadgetTool with.",
            ["players"] = players,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Unlocked gadgets in a random order, each kept when the plate can still take it.</summary>
    private static GadgetPlate Fill(GadgetCatalog catalog, PlayerSetups player, Random random)
    {
        var pool = catalog.Gadgets.Where(g => player.Unlocked.Contains(g.Id)).OrderBy(_ => random.Next()).ToList();
        var chosen = new List<GadgetDefinition>();
        foreach (var gadget in pool)
        {
            var costs = chosen.Select(g => g.SlotCost).Append(gadget.SlotCost).ToList();
            if (player.Layout.Arrange(costs) is not null)
                chosen.Add(gadget);
            if (chosen.Sum(g => g.SlotCost) >= player.Layout.SlotCount)
                break;
        }

        string? super = null;
        if (player.Layout.HasSuperSlot)
            super = pool.FirstOrDefault(g => g.SlotCost == 1 && !chosen.Contains(g))?.Id;

        var plate = new GadgetPlate(chosen.Select(g => g.Id), super);
        // Built to follow the rules; checked anyway, an empty plate if not.
        return PlateValidator.Validate(plate, player, catalog).Count == 0 ? plate : GadgetPlate.Empty;
    }
}
