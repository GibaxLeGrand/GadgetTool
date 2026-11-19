using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Rules;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Tests;

/// <summary>The sample save made from a gadget list, for releases that ship the game's data.</summary>
public sealed class SampleSaveTests
{
    private static GadgetCatalog DevCatalog() =>
        CatalogJson.Parse(File.ReadAllText(TestFiles.Repo("data/dev/catalog.json"))).For(null);

    [Fact]
    public void Four_players_on_four_plate_levels_with_valid_presets_of_known_gadgets()
    {
        var catalog = DevCatalog();
        var players = new DevJsonSaveFormat().Read(System.Text.Encoding.UTF8.GetBytes(SampleSave.Generate(catalog))).Players;

        Assert.Equal([1, 2, 3, 4], players.Select(p => p.PlayerNumber));
        Assert.Equal([7, 6, 5, 3], players.Select(p => p.Layout.SlotCount + (p.Layout.HasSuperSlot ? 1 : 0)));
        Assert.All(players.SelectMany(p => p.Unlocked), id => Assert.True(catalog.TryGet(id, out _), id));
        Assert.All(players, p => Assert.Equal(catalog.Gadgets.Count, p.Unlocked.Count)); // every gadget unlocked (Gibax)

        foreach (var player in players)
        {
            Assert.Equal(10, player.Plates.Count);
            Assert.All(player.Plates.Take(6), plate => Assert.NotEmpty(plate.GadgetIds));
            Assert.All(player.Plates.Skip(6), plate => Assert.Empty(plate.GadgetIds));
            Assert.All(player.Plates, plate => Assert.Empty(PlateValidator.Validate(plate, player, catalog)));
        }
        Assert.Contains(players[0].Plates, plate => plate.SuperGadgetId is not null);
    }

    [Fact]
    public void The_same_list_gives_the_same_save() =>
        Assert.Equal(SampleSave.Generate(DevCatalog()), SampleSave.Generate(DevCatalog()));
}

/// <summary>Files of the repository, found from the test's build folder.</summary>
internal static class TestFiles
{
    public static string Repo(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException(relative);
    }
}
