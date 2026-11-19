using System.Text;
using System.Text.Json.Nodes;
using GadgetTool.Core.Editing;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Setups;
using static GadgetTool.Core.Tests.TestData;

namespace GadgetTool.Core.Tests;

public class DevJsonSaveFormatTests
{
    private static readonly DevJsonSaveFormat Format = new();
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes(DevSaveJson);

    [Fact]
    public void Recognises_its_own_saves_only()
    {
        Assert.True(Format.CanRead(Bytes));
        Assert.False(Format.CanRead("GVAS\0\0\0\0"u8));
        Assert.False(Format.CanRead("""{ "devSave": "other" }"""u8));
        Assert.False(Format.CanRead("""{ "devSave": 3 }"""u8));
    }

    [Fact]
    public void Reads_players_plates_and_super_slot()
    {
        var save = Format.Read(Bytes);

        Assert.Equal("dev-1", save.SaveVersion);
        var p1 = save.Player(1);
        Assert.True(p1.Layout.SameAs(PlateLayout.GrandMaster));
        Assert.Equal(2, p1.Plates.Count);
        Assert.True(p1.Plates[0].SameGadgetsAs(new GadgetPlate([TwoA, OneA], superGadgetId: OneB)));
        Assert.Contains(ThreeA, p1.Unlocked);

        var p2 = save.Player(2);
        Assert.False(p2.Layout.HasSuperSlot);
        Assert.Equal([3, 1], p2.Layout.RowSizes);
    }

    [Fact]
    public void Writes_the_plates_and_keeps_everything_else()
    {
        var edited = Format.Read(Bytes)
            .WithPlate(new PlateRef(1, 0), Plate(ThreeA))
            .WithPlate(new PlateRef(2, 1), Plate(OneA));

        var written = JsonNode.Parse(Format.Write(Bytes, edited))!;
        var original = JsonNode.Parse(DevSaveJson)!;

        var p1Plate = written["players"]![0]!["plates"]![0]!;
        Assert.Equal([ThreeA], p1Plate["gadgets"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(p1Plate["super"]);
        Assert.Equal("kept", p1Plate["name"]!.GetValue<string>());
        Assert.Equal("kept too", written["players"]![0]!["favourite"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(original["progress"], written["progress"]));
        Assert.True(JsonNode.DeepEquals(original["players"]![0]!["unlocked"], written["players"]![0]!["unlocked"]));

        Assert.True(Format.Read(Format.Write(Bytes, edited)).Player(2).Plates[1].SameGadgetsAs(Plate(OneA)));
    }

    [Fact]
    public void Refuses_an_unknown_version()
    {
        var newer = Encoding.UTF8.GetBytes(DevSaveJson.Replace("\"dev-1\"", "\"dev-2\"", StringComparison.Ordinal));

        Assert.Throws<SaveFormatException>(() => Format.Read(newer));
        Assert.Throws<SaveFormatException>(() => Format.Write(newer, Format.Read(Bytes)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "devSave": "gadgettool", "version": "dev-1" }""")]
    [InlineData("""{ "devSave": "gadgettool", "version": "dev-1", "players": [ { "player": 5, "rows": [3], "unlocked": [], "plates": [] } ] }""")]
    [InlineData("""{ "devSave": "gadgettool", "version": "dev-1", "players": [ { "player": 1, "rows": [4], "unlocked": [], "plates": [] } ] }""")]
    [InlineData("""{ "devSave": "gadgettool", "version": "dev-1", "players": [ { "player": 1, "rows": [3], "unlocked": [], "plates": [ { "gadgets": [1] } ] } ] }""")]
    public void Refuses_a_damaged_save(string json) =>
        Assert.Throws<SaveFormatException>(() => Format.Read(Encoding.UTF8.GetBytes(json)));
}

public class SafeSaveWriterTests
{
    private static readonly FixedTime Now = new(new DateTimeOffset(2026, 11, 19, 14, 30, 5, TimeSpan.Zero));

    [Fact]
    public void Backs_up_then_replaces_the_save()
    {
        using var temp = new TempFolder();
        var save = temp.File("save.json");
        File.WriteAllText(save, "old");

        var backup = new SafeSaveWriter(new FakeGame(false), Now)
            .Write(save, "old"u8, "new"u8, temp.File("backups"));

        Assert.Equal("new", File.ReadAllText(save));
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.Equal("save.json.20261119-143005.bak", Path.GetFileName(backup));
        Assert.False(File.Exists(save + ".gadgettool.tmp"));
    }

    [Fact]
    public void Never_writes_while_the_game_runs()
    {
        using var temp = new TempFolder();
        var save = temp.File("save.json");
        File.WriteAllText(save, "old");

        Assert.Throws<GameRunningException>(() =>
            new SafeSaveWriter(new FakeGame(true), Now).Write(save, "old"u8, "new"u8, temp.File("backups")));

        Assert.Equal("old", File.ReadAllText(save));
        Assert.False(Directory.Exists(temp.File("backups")));
    }

    [Fact]
    public void Never_writes_over_a_save_that_changed_since_it_was_read()
    {
        using var temp = new TempFolder();
        var save = temp.File("save.json");
        File.WriteAllText(save, "written by the game meanwhile");

        Assert.Throws<SaveChangedException>(() =>
            new SafeSaveWriter(new FakeGame(false), Now).Write(save, "old"u8, "new"u8, temp.File("backups")));

        Assert.Equal("written by the game meanwhile", File.ReadAllText(save));
    }

    [Fact]
    public void Two_saves_in_the_same_second_keep_both_backups()
    {
        using var temp = new TempFolder();
        var save = temp.File("save.json");
        File.WriteAllText(save, "v1");
        var writer = new SafeSaveWriter(new FakeGame(false), Now);

        var first = writer.Write(save, "v1"u8, "v2"u8, temp.File("backups"));
        var second = writer.Write(save, "v2"u8, "v3"u8, temp.File("backups"));

        Assert.NotEqual(first, second);
        Assert.Equal("v1", File.ReadAllText(first));
        Assert.Equal("v2", File.ReadAllText(second));
    }
}

public class SaveSessionTests
{
    private static readonly FixedTime Now = new(new DateTimeOffset(2026, 11, 19, 14, 30, 5, TimeSpan.Zero));

    private static (TempFolder Temp, SaveSession Session) Open(string json = DevSaveJson)
    {
        var temp = new TempFolder();
        File.WriteAllText(temp.File("save.json"), json);
        return (temp, SaveSession.Open(temp.File("save.json"), new DevJsonSaveFormat(), Catalog));
    }

    [Fact]
    public void Edit_save_and_read_back()
    {
        var (temp, session) = Open();
        using var _ = temp;

        var pasted = SetupEditor.Copy(session.Current, new PlateRef(1, 0), new PlateRef(1, 1), Catalog);
        session.Update(pasted.Save);
        Assert.Equal([new PlateRef(1, 1)], session.ChangedPlates);

        var backup = session.Save(new SafeSaveWriter(new FakeGame(false), Now), temp.File("backups"));

        Assert.False(session.HasChanges);
        Assert.True(File.Exists(backup));
        var reread = SaveSession.Open(temp.File("save.json"), new DevJsonSaveFormat(), Catalog);
        Assert.True(reread.Saved.Plate(new PlateRef(1, 1)).SameGadgetsAs(new GadgetPlate([TwoA, OneA], superGadgetId: OneB)));
    }

    [Fact]
    public void Unlocks_cannot_be_edited()
    {
        var (temp, session) = Open();
        using var _ = temp;

        var p2 = session.Current.Player(2);
        var cheated = session.Current with
        {
            Players = [session.Current.Player(1), p2 with { Unlocked = new HashSet<string>(AllButLocked) }],
        };

        Assert.Throws<InvalidOperationException>(() => session.Update(cheated));
    }

    [Fact]
    public void The_plate_level_cannot_be_edited()
    {
        var (temp, session) = Open();
        using var _ = temp;

        var p2 = session.Current.Player(2);
        var cheated = session.Current with
        {
            Players = [session.Current.Player(1), p2 with { Layout = PlateLayout.GrandMaster }],
        };

        Assert.Throws<InvalidOperationException>(() => session.Update(cheated));
    }

    [Fact]
    public void A_changed_plate_that_breaks_a_rule_is_never_written()
    {
        var (temp, session) = Open();
        using var _ = temp;

        // Bypasses SetupEditor: P2 does not own TwoA.
        session.Update(session.Current.WithPlate(new PlateRef(2, 1), Plate(TwoA)));

        Assert.Throws<InvalidOperationException>(() =>
            session.Save(new SafeSaveWriter(new FakeGame(false), Now), temp.File("backups")));
        Assert.Equal(DevSaveJson, File.ReadAllText(temp.File("save.json")));
    }

    [Fact]
    public void An_untouched_plate_already_broken_in_the_save_does_not_block_saving()
    {
        // P2's preset A holds a gadget P2 does not own, as a game patch could leave it.
        var json = DevSaveJson.Replace("""{ "gadgets": ["t.one_a"] }, { "gadgets": [] }""", """{ "gadgets": ["t.two_a"] }, { "gadgets": [] }""", StringComparison.Ordinal);
        var (temp, session) = Open(json);
        using var _ = temp;

        Assert.Single(session.Problems());
        session.Update(SetupEditor.Paste(session.Current, Plate(OneA), new PlateRef(1, 1), Catalog).Save);

        session.Save(new SafeSaveWriter(new FakeGame(false), Now), temp.File("backups"));
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_a_save()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("notes.txt"), "hello");

        Assert.Throws<SaveFormatException>(() => SaveSession.Open(temp.File("notes.txt"), new DevJsonSaveFormat(), Catalog));
    }
}
