using System.Diagnostics;
using System.Globalization;

namespace GadgetTool.Core.Saves;

/// <summary>Tells whether the game is running.</summary>
public interface IGameProcessProbe
{
    bool IsGameRunning();
}

/// <summary>
/// Looks for running processes by name (without ".exe"). The game's process name is not known
/// yet: the app passes it once it has been seen on a machine with the game.
/// </summary>
public sealed class ProcessNameProbe(IEnumerable<string> processNames) : IGameProcessProbe
{
    private readonly string[] _names = [.. processNames];

    public bool IsGameRunning() => _names.Any(name =>
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    });
}

public sealed class GameRunningException()
    : Exception("Close the game before saving: it would overwrite the changes, and Steam Cloud could sync a half-written save.");

public sealed class SaveChangedException(string path)
    : Exception($"'{path}' changed since it was opened (the game or Steam Cloud wrote it). Reopen it and redo the changes.");

/// <summary>
/// Writes a save the careful way: never while the game runs, never over a save that changed
/// since it was read, always after a timestamped backup, and through a temporary file so a
/// crash mid-write cannot leave half a save.
/// </summary>
public sealed class SafeSaveWriter(IGameProcessProbe game, TimeProvider time)
{
    /// <param name="savePath">The save to replace.</param>
    /// <param name="expectedContent">The bytes the save held when it was read.</param>
    /// <param name="newContent">The bytes to write.</param>
    /// <param name="backupDirectory">Where the backup goes (created if missing).</param>
    /// <returns>The backup's path.</returns>
    /// <exception cref="GameRunningException">The game is running.</exception>
    /// <exception cref="SaveChangedException">The save no longer holds <paramref name="expectedContent"/>.</exception>
    public string Write(string savePath, ReadOnlySpan<byte> expectedContent, ReadOnlySpan<byte> newContent, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(savePath);
        ArgumentException.ThrowIfNullOrEmpty(backupDirectory);

        if (game.IsGameRunning())
            throw new GameRunningException();
        if (!File.ReadAllBytes(savePath).AsSpan().SequenceEqual(expectedContent))
            throw new SaveChangedException(savePath);

        Directory.CreateDirectory(backupDirectory);
        var backupPath = FreeBackupPath(savePath, backupDirectory);
        File.Copy(savePath, backupPath, overwrite: false);

        var tempPath = savePath + ".gadgettool.tmp";
        File.WriteAllBytes(tempPath, newContent);
        File.Move(tempPath, savePath, overwrite: true);
        return backupPath;
    }

    private string FreeBackupPath(string savePath, string backupDirectory)
    {
        var stem = $"{Path.GetFileName(savePath)}.{time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        var path = Path.Combine(backupDirectory, stem + ".bak");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(backupDirectory, $"{stem}-{n}.bak");
        return path;
    }
}
