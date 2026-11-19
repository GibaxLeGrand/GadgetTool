using System.Text.Json.Nodes;
using GadgetTool.Core.Gadgets;

namespace GadgetTool.Extractor.Tests;

/// <summary>
/// Tables shaped like CUE4Parse's JSON for the game's DataTables, with made-up gadgets: no id,
/// name or text here comes from the game.
/// </summary>
public class PackBuilderTests
{
    private static readonly PackInfo Info = new(new GameVersion(2, 0, 0), "12345");

    private static JsonObject Text(string key, string? localized) => new()
    {
        ["TableId"] = "/Game/Test/DB_Test.DB_Test",
        ["Key"] = key,
        ["SourceString"] = key,
        ["LocalizedString"] = localized,
    };

    private static JsonObject NoText() => new() { ["CultureInvariantString"] = null };

    private static JsonObject Gadget(string id, int sizeX, string category, int sort, string? name, string? description = null,
        string? super = null, bool enabled = true, bool invisible = false, int sizeY = 1) => new()
    {
        ["bIsEnabled"] = enabled,
        ["ID"] = "EGadgetId::" + id,
        ["Category"] = "EGadgetCategory::" + category,
        ["Name"] = Text("name_" + id, name),
        ["Explanation"] = description is null ? NoText() : Text("guide_" + id, description),
        ["ExplanationSpecial"] = super is null ? NoText() : Text("sp_" + id, super),
        ["IconTexture"] = new JsonObject { ["AssetPathName"] = $"/Game/Test/Icons/Icon_{id}.Icon_{id}", ["SubPathString"] = "" },
        ["SizeX"] = sizeX,
        ["SizeY"] = sizeY,
        ["plateId"] = "EGadgetPlateId::Rank2",
        ["GadgetCustomSortId"] = sort,
        ["bInvisibleGadgetCustom"] = invisible,
    };

    private static JsonObject Category(string name, int sort) => new()
    {
        ["CategoryName"] = Text("type_" + name, name),
        ["CategoryColor"] = new JsonObject { ["R"] = 1.0, ["Hex"] = "ABCDEF" },
        ["SortID"] = sort,
    };

    private static JsonObject Rows(params (string Id, JsonObject Row)[] rows)
    {
        var json = new JsonObject();
        foreach (var (id, row) in rows)
            json[id] = row;
        return json;
    }

    /// <summary>English and French tables: three gadgets in two categories, one disabled.</summary>
    private static GameTables Tables(Func<string, JsonObject>? gadgets = null, Func<string, JsonObject>? categories = null)
    {
        JsonObject DefaultGadgets(string lang) => Rows(
            ("EGadgetId::Speed_B", Gadget("Speed_B", 2, "Speed", sort: 2, lang == "en" ? "Speed B" : "Vitesse B", lang == "en" ? "Go faster." : "Va plus vite.")),
            ("EGadgetId::Speed_A", Gadget("Speed_A", 1, "Speed", sort: 1, lang == "en" ? "Speed A" : "Vitesse A", super: lang == "en" ? "Even faster." : "Encore plus vite.", invisible: true)),
            ("EGadgetId::Ring_A", Gadget("Ring_A", 3, "Rings", sort: 5, lang == "en" ? "Ring A" : null)),
            ("EGadgetId::Old", Gadget("Old", 1, "Rings", sort: 0, "Old", enabled: false)),
            ("EGadgetId::NonGadget", Gadget("NonGadget", 1, "Rings", sort: 0, null, enabled: false)));
        JsonObject DefaultCategories(string lang) => Rows(
            ("EGadgetCategory::Rings", Category(lang == "en" ? "Rings" : "Anneaux", 1)),
            ("EGadgetCategory::Speed", Category(lang == "en" ? "Speed" : "Vitesse", 2)),
            ("EGadgetCategory::Unused", Category("Unused", 3)));

        gadgets ??= DefaultGadgets;
        categories ??= DefaultCategories;
        return new GameTables("en",
            new Dictionary<string, JsonObject> { ["en"] = gadgets("en"), ["fr"] = gadgets("fr") },
            new Dictionary<string, JsonObject> { ["en"] = categories("en"), ["fr"] = categories("fr") });
    }

    [Fact]
    public void Writes_a_catalog_the_app_reads()
    {
        var catalog = PackBuilder.BuildCatalog(Tables(), Info);
        var parsed = CatalogJson.Parse(catalog.ToJsonString());

        Assert.Equal(new GameVersion(2, 0, 0), parsed.GameVersion);
        Assert.Equal(["en", "fr"], parsed.Languages);
        Assert.Equal("12345", (string?)catalog["steamBuildId"]);

        // Enabled gadgets only (invisible ones included), by category order, then the game's sort.
        var gadgets = parsed.For("fr").Gadgets;
        Assert.Equal(["Ring_A", "Speed_A", "Speed_B"], gadgets.Select(g => g.Id));
        Assert.Equal([3, 1, 2], gadgets.Select(g => g.SlotCost));
        Assert.Equal(["Rings", "Speed", "Speed"], gadgets.Select(g => g.CategoryKey));
        Assert.Equal(["Ring A", "Vitesse A", "Vitesse B"], gadgets.Select(g => g.Name)); // no French name → English
        Assert.Equal(["Anneaux", "Vitesse", "Vitesse"], gadgets.Select(g => g.Category));
        Assert.Equal("Va plus vite.", gadgets[2].Description);
    }

    [Fact]
    public void Keeps_super_effects_plates_and_icons_and_drops_unresolved_texts()
    {
        var gadgets = PackBuilder.BuildCatalog(Tables(), Info)["gadgets"]!.AsArray().Cast<JsonObject>().ToDictionary(g => (string)g["id"]!);

        Assert.Equal("Encore plus vite.", (string?)gadgets["Speed_A"]["superEffect"]!["fr"]);
        Assert.Null(gadgets["Speed_B"]["superEffect"]);
        Assert.Equal("Rank2", (string?)gadgets["Speed_B"]["plate"]);
        Assert.Equal("/Game/Test/Icons/Icon_Speed_B", (string?)gadgets["Speed_B"]["icon"]);
        Assert.Null(gadgets["Ring_A"]["name"]!["fr"]); // the French text was missing
    }

    [Fact]
    public void Lists_only_the_categories_that_hold_gadgets_with_their_colours()
    {
        var categories = PackBuilder.BuildCatalog(Tables(), Info)["categories"]!.AsArray().Cast<JsonObject>().ToList();

        Assert.Equal(["Rings", "Speed"], categories.Select(c => (string)c["id"]!));
        Assert.Equal("#ABCDEF", (string?)categories[0]["color"]);
        Assert.Equal("Anneaux", (string?)categories[0]["name"]!["fr"]);
    }

    [Theory]
    [InlineData("gadget_name_x", "gadget_name_x", null)]   // an unresolved string table key
    [InlineData("gadget_name_x", "", null)]
    [InlineData("gadget_name_x", "Ring Magnet", "Ring Magnet")]
    public void A_text_is_kept_only_when_resolved(string key, string localized, string? expected) =>
        Assert.Equal(expected, PackBuilder.Resolved(Text(key, localized)));

    [Fact]
    public void Refuses_data_the_app_could_not_use_and_lists_every_problem()
    {
        JsonObject Bad(string lang) => Rows(
            ("EGadgetId::Wide", Gadget("Wide", 4, "Speed", 1, "Wide")),
            ("EGadgetId::Tall", Gadget("Tall", 1, "Speed", 2, "Tall", sizeY: 2)),
            ("EGadgetId::Twin", Gadget("Twin", 1, "Speed", 3, "Twin")),
            ("EGadgetId::twin", Gadget("twin", 1, "Speed", 4, "twin")),
            ("EGadgetId::Lost", Gadget("Lost", 1, "Nowhere", 5, "Lost")),
            ("EGadgetId::Mute", Gadget("Mute", 1, "Speed", 6, null)),
            ("EGadgetId::Bad/Name", Gadget("Bad/Name", 1, "Speed", 7, "Bad")));

        var e = Assert.Throws<PackException>(() => PackBuilder.BuildCatalog(Tables(gadgets: Bad), Info));

        Assert.Equal(6, e.Problems.Count);
        Assert.Contains(e.Problems, p => p.Contains("'Bad/Name' has characters other than"));
        Assert.Contains(e.Problems, p => p.Contains("'Wide' takes 4 slots"));
        Assert.Contains(e.Problems, p => p.Contains("'Tall' is 2 rows high"));
        Assert.Contains(e.Problems, p => p.Contains("'twin' appears twice"));
        Assert.Contains(e.Problems, p => p.Contains("category 'Nowhere'"));
        Assert.Contains(e.Problems, p => p.Contains("'Mute' has no name in 'en'"));
    }

    [Fact]
    public void Each_gadget_gets_the_base_the_game_draws_it_on()
    {
        JsonObject Rows3(string lang) => Rows(
            ("EGadgetId::Speed_A", Gadget("Speed_A", 1, "Speed", 1, "Speed A")),
            ("EGadgetId::Speed_Mini", Gadget("Speed_Mini", 1, "Speed", 2, "Speed Mini")),
            ("EGadgetId::Big_Kit", Gadget("Big_Kit", 3, "StarterKit", 3, "Big Kit")),
            ("EGadgetId::Ex_Kit", Gadget("Ex_Kit", 3, "AdvancedKit", 4, "Ex Kit")));
        JsonObject Categories3(string lang) => Rows(
            ("EGadgetCategory::Speed", Category("Speed", 1)),
            ("EGadgetCategory::StarterKit", Category("Starter Kit", 2)),
            ("EGadgetCategory::AdvancedKit", Category("Advanced Kit", 3)));
        var tables = Tables(Rows3, Categories3) with { MiniGadgets = ["EGadgetId::Speed_Mini"] };

        var gadgets = PackBuilder.BuildCatalog(tables, Info)["gadgets"]!.AsArray().Cast<JsonObject>().ToDictionary(g => (string)g["id"]!);

        Assert.Null(gadgets["Speed_A"]["base"]);
        Assert.Equal("mini", (string?)gadgets["Speed_Mini"]["base"]);
        Assert.Equal("kit", (string?)gadgets["Big_Kit"]["base"]);
        Assert.Equal("kit", (string?)gadgets["Ex_Kit"]["base"]); // advanced kits too have the kit frame
    }

    [Fact]
    public void The_ui_folder_holds_every_picture_the_page_asks_for() =>
        Assert.Equal(
            ["base-1.png", "base-2.png", "base-3.png", "base-kit-1.png", "base-kit-3.png", "base-mini.png", "base-super.png", "lock.png", "plus.png"],
            PackBuilder.UiTextures.Keys.Order(StringComparer.Ordinal));

    private static JsonObject Plate(string id, int slots, bool super, int frame, string? name) => new()
    {
        ["ID"] = "EGadgetPlateId::" + id,
        ["SlotNum"] = slots,
        ["bSpSlotOpen"] = super,
        ["PresetCount"] = 10,
        ["BaseIndex"] = frame,
        ["Name"] = Text("plate_" + id, name),
    };

    [Fact]
    public void Keeps_the_plate_levels_with_their_picture_and_where_things_go_on_it()
    {
        var tables = Tables() with
        {
            PlateRows = new Dictionary<string, JsonObject>
            {
                ["en"] = Rows(("Rank0", Plate("Rank0", 2, false, 0, "Normal")), ("Rank7", Plate("Rank7", 6, true, 6, "Top"))),
                ["fr"] = Rows(("Rank0", Plate("Rank0", 2, false, 0, "Normale")), ("Rank7", Plate("Rank7", 6, true, 6, null))),
            },
        };
        var parsed = CatalogJson.Parse(PackBuilder.BuildCatalog(tables, Info).ToJsonString());

        Assert.Equal(["Rank0", "Rank7"], parsed.Plates.Select(p => p.Id));
        Assert.Equal([2, 6], parsed.Plates.Select(p => p.Slots));
        Assert.Equal([false, true], parsed.Plates.Select(p => p.SuperSlot));
        Assert.Equal(["ui/plate-0.png", "ui/plate-6.png"], parsed.Plates.Select(p => p.Image));
        Assert.Equal("Normale", parsed.Plates[0].Name.Get("fr", "en"));
        Assert.Equal("Top", parsed.Plates[1].Name.Get("fr", "en")); // no French name: English

        // Every cell, the Super Slot and the title are inside the cropped picture.
        var g = parsed.PlateGeometry!;
        Assert.Equal((PackBuilder.PlateCrop.Width, PackBuilder.PlateCrop.Height), (g.Width, g.Height));
        Assert.InRange(g.FirstX - g.Cell / 2, 0, g.Width);
        Assert.InRange(g.FirstY - g.Cell / 2, 0, g.Height);
        Assert.InRange(g.FirstX + 2 * g.StepX + g.Cell / 2, 0, g.Width);
        Assert.InRange(g.FirstY + g.StepY + g.Cell / 2, 0, g.Height);
        Assert.InRange(g.SuperX + g.Cell / 2, 0, g.Width);
        Assert.InRange(g.SuperY + g.Cell / 2, 0, g.Height);
        Assert.InRange(g.TitleRight, g.TitleLeft, g.Width);
    }

    [Fact]
    public void Without_the_plate_table_the_catalog_has_no_plates()
    {
        var catalog = PackBuilder.BuildCatalog(Tables(), Info);
        Assert.Null(catalog["plates"]);
        Assert.Null(catalog["plateGeometry"]);
    }

    [Fact]
    public void Refuses_a_plate_whose_picture_is_not_in_the_atlas()
    {
        var tables = Tables() with
        {
            PlateRows = new Dictionary<string, JsonObject> { ["en"] = Rows(("Rank9", Plate("Rank9", 6, true, PackBuilder.PlateFrames, "Bad"))) },
        };
        var error = Assert.Throws<PackException>(() => PackBuilder.BuildCatalog(tables, Info));
        Assert.Contains(error.Problems, p => p.Contains("Rank9", StringComparison.Ordinal));
    }

    [Fact]
    public void The_super_masks_are_the_super_base_s_and_do_not_clash_with_other_pictures() =>
        Assert.Multiple(
            () => Assert.Equal(["super-mask.png", "super-outline.png"], PackBuilder.SuperMasks.Keys.Order(StringComparer.Ordinal)),
            () => Assert.All(PackBuilder.SuperMasks.Values, t => Assert.StartsWith(PackBuilder.UiTextures["base-super.png"] + "_", t, StringComparison.Ordinal)),
            () => Assert.Empty(PackBuilder.SuperMasks.Keys.Intersect(PackBuilder.UiTextures.Keys)));

    [Fact]
    public void The_plate_crop_is_inside_every_frame_of_the_atlas()
    {
        var (x, y, w, h) = PackBuilder.PlateCrop;
        Assert.True(x >= 0 && y >= 0);
        Assert.InRange(x + w, 1, PackBuilder.PlateFrameWidth);
        Assert.InRange(y + h, 1, PackBuilder.PlateFrameHeight);
    }

    [Fact]
    public void Maps_game_asset_paths_to_pak_paths()
    {
        Assert.Equal("UNION/Content/05_Union/UI/Icon_A", PackBuilder.PackagePath("/Game/05_Union/UI/Icon_A"));
        Assert.Throws<ArgumentException>(() => PackBuilder.PackagePath("/Engine/Icon_A"));
    }

    [Fact]
    public void The_index_entry_lists_every_file_of_the_pack_with_its_hash()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gadgettool-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "icons"));
            File.WriteAllText(Path.Combine(folder, "catalog.json"), "{}");
            File.WriteAllBytes(Path.Combine(folder, "icons", "Speed_A.png"), [1, 2, 3]);

            var entry = PackBuilder.IndexEntry(new GameVersion(2, 0, 0), "12345", folder);

            Assert.Equal("2.0.0", entry.Folder);
            Assert.Equal(["12345"], entry.SteamBuildIds);
            Assert.Equal(["catalog.json", "icons/Speed_A.png"], entry.Files.Keys.Order(StringComparer.Ordinal));
            // SHA-256 of "{}" and of the bytes 1, 2, 3.
            Assert.Equal("44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a", entry.Files["catalog.json"]);
            Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", entry.Files["icons/Speed_A.png"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
