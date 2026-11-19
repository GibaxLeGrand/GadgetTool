using GadgetTool.Core.Gadgets;
using GadgetTool.Core.Saves;
using GadgetTool.Core.Setups;

namespace GadgetTool.Core.Tests;

/// <summary>Made-up gadgets and saves. No id or name here comes from the game.</summary>
internal static class TestData
{
    public const string OneA = "t.one_a";
    public const string OneB = "t.one_b";
    public const string OneC = "t.one_c";
    public const string OneD = "t.one_d";
    public const string TwoA = "t.two_a";
    public const string TwoB = "t.two_b";
    public const string TwoC = "t.two_c";
    public const string ThreeA = "t.three_a";
    public const string Locked = "t.one_locked";

    public static GadgetCatalog Catalog { get; } = new(
    [
        new(OneA, "One A", 1), new(OneB, "One B", 1), new(OneC, "One C", 1), new(OneD, "One D", 1),
        new(TwoA, "Two A", 2), new(TwoB, "Two B", 2), new(TwoC, "Two C", 2),
        new(ThreeA, "Three A", 3),
        new(Locked, "Locked", 1),
    ]);

    public static string[] AllButLocked => [OneA, OneB, OneC, OneD, TwoA, TwoB, TwoC, ThreeA];

    public static PlayerSetups Player(int number, PlateLayout layout, IEnumerable<string>? unlocked = null, int presets = 10) =>
        new(number, layout, (unlocked ?? AllButLocked).ToHashSet(StringComparer.Ordinal),
            Enumerable.Repeat(GadgetPlate.Empty, presets).ToArray());

    /// <summary>P1: Grand Master plate, everything but <see cref="Locked"/>. P2: Master plate, a few gadgets.</summary>
    public static SaveSetups Save() => new("dev-1",
    [
        Player(1, PlateLayout.GrandMaster),
        Player(2, PlateLayout.Master, [OneA, OneB, TwoA]),
    ]);

    public static GadgetPlate Plate(params string[] ids) => new(ids);

    public const string DevSaveJson = """
        {
          "devSave": "gadgettool",
          "version": "dev-1",
          "players": [
            {
              "player": 1, "rows": [3, 3], "superSlot": true,
              "unlocked": ["t.one_a", "t.one_b", "t.two_a", "t.three_a"],
              "plates": [
                { "gadgets": ["t.two_a", "t.one_a"], "super": "t.one_b", "name": "kept" },
                { "gadgets": [] }
              ],
              "favourite": "kept too"
            },
            {
              "player": 2, "rows": [3, 1],
              "unlocked": ["t.one_a"],
              "plates": [ { "gadgets": ["t.one_a"] }, { "gadgets": [] } ]
            }
          ],
          "progress": { "races": 120, "tickets": [1, 2, 3] }
        }
        """;
}

internal sealed class FakeGame(bool running) : IGameProcessProbe
{
    public bool IsGameRunning() => running;
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>A folder under the system temp folder, deleted after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gadgettool-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Left for the system to clean; a test must not fail on cleanup.
        }
    }
}
