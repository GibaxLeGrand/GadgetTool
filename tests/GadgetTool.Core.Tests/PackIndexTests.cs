using GadgetTool.Core.Gadgets;

namespace GadgetTool.Core.Tests;

public class PackIndexTests
{
    private static PackEntry Pack(byte minor, params string[] builds) =>
        new(new GameVersion(2, minor, 0), builds, $"2.{minor}.0", new Dictionary<string, string> { ["catalog.json"] = "ab12" });

    [Fact]
    public void Picks_the_pack_of_the_installed_build_else_the_newest()
    {
        var index = new PackIndex([Pack(0, "100", "101"), Pack(1, "200")]);

        Assert.Equal("2.0.0", index.Pick("101", out var exact)!.Folder);
        Assert.True(exact);

        Assert.Equal("2.1.0", index.Pick("999", out exact)!.Folder);
        Assert.False(exact);
        Assert.Equal("2.1.0", index.Pick(null, out exact)!.Folder);
        Assert.False(exact);

        Assert.Null(PackIndex.Empty.Pick("100", out _));
    }

    [Fact]
    public void A_new_pack_for_a_known_version_replaces_it_and_keeps_its_builds()
    {
        var index = new PackIndex([Pack(0, "100"), Pack(1, "200")]).With(Pack(0, "150"));

        Assert.Equal(["2.1.0", "2.0.0"], index.Packs.Select(p => p.Folder));
        Assert.Equal(["100", "150"], index.Packs[1].SteamBuildIds);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var index = new PackIndex([Pack(0, "100"), Pack(1, "200")]);

        var again = PackIndex.Parse(index.ToJson());

        Assert.Equal(index.Packs.Select(p => p.GameVersion), again.Packs.Select(p => p.GameVersion));
        Assert.Equal(["200"], again.Packs[0].SteamBuildIds);
        Assert.Equal("ab12", again.Packs[1].Files["catalog.json"]);
    }

    [Theory]
    [InlineData("""{ "format": "other", "version": 1, "packs": [] }""")]
    [InlineData("""{ "format": "gadgettool.packs", "version": 9, "packs": [] }""")]
    [InlineData("""{ "format": "gadgettool.packs", "version": 1, "packs": [ { "gameVersion": "2.0.0", "folder": "../outside" } ] }""")]
    [InlineData("""{ "format": "gadgettool.packs", "version": 1, "packs": [ { "gameVersion": "2.0.0", "folder": "C:\\data" } ] }""")]
    [InlineData("""{ "format": "gadgettool.packs", "version": 1, "packs": [ { "gameVersion": "x", "folder": "a" } ] }""")]
    [InlineData("not json")]
    public void Refuses_what_it_cannot_trust(string json) =>
        Assert.Throws<FormatException>(() => PackIndex.Parse(json));
}

public class SteamInstallTests
{
    private const string Manifest = "\"AppState\"\n{\n\t\"appid\"\t\t\"2486820\"\n\t\"buildid\"\t\t\"24680\"\n\t\"LastOwner\"\t\t\"0\"\n}\n";

    private const string Libraries = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Steam"
        		"apps"
        		{
        			"620"		"1"
        		}
        	}
        	"1"
        	{
        		"path"		"E:\\"
        		"apps"
        		{
        			"2486820"		"2"
        		}
        	}
        }
        """;

    [Fact]
    public void Reads_the_build_id_of_an_app_manifest()
    {
        Assert.Equal("24680", SteamInstall.BuildId(Manifest));
        Assert.Null(SteamInstall.BuildId("\"AppState\" { }"));
    }

    [Fact]
    public void Finds_the_library_that_holds_the_game() =>
        // "E:\\" in the file is E:\ (a library at a drive's root ends with an escaped backslash).
        Assert.Equal([@"E:\"], SteamInstall.LibrariesWith("2486820", Libraries));

    [Fact]
    public void Finds_the_installed_build_through_the_libraries()
    {
        var root = Path.Combine(Path.GetTempPath(), "gadgettool-steam-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "lib2");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            Directory.CreateDirectory(Path.Combine(library, "steamapps"));
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                Libraries.Replace(@"E:\\", library.Replace(@"\", @"\\", StringComparison.Ordinal), StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_2486820.acf"), Manifest);

            Assert.Equal("24680", SteamInstall.FindBuildId(root));
            Assert.Null(SteamInstall.FindBuildId(Path.Combine(root, "nowhere")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
