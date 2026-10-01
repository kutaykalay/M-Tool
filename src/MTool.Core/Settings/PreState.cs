using System.Text.Json;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Core.Settings;

public sealed record RegisterValue(byte Register, byte Value);

/// <summary>
/// Every writable register as it was before M-Tool's first write (plan.md §4.3). Not the factory
/// state: YAMDCC had already written some of these values.
/// </summary>
public sealed record PreMToolState(
    string FirmwareVersion,
    string FirmwareDate,
    DateTimeOffset CapturedAt,
    IReadOnlyList<RegisterValue> Registers);

public static class PreStateCapture
{
    /// <summary>
    /// Reads every writable register twice and fails if the passes disagree, so a byte mangled by
    /// concurrent EC traffic can never become the permanent backup.
    /// </summary>
    public static PreMToolState Read(IEcRegisters ec, FirmwareInfo firmware, DateTimeOffset now)
    {
        var registers = EcWriteRules.WritableRegisters;
        var first = registers.Select(ec.Read).ToArray();
        var second = registers.Select(ec.Read).ToArray();

        var mismatch = registers.Zip(first, second).FirstOrDefault(t => t.Second != t.Third);
        if (mismatch != default)
        {
            throw new EcAccessException(
                $"Yedek alınamadı: 0x{mismatch.First:X2} iki okumada farklı (0x{mismatch.Second:X2} / 0x{mismatch.Third:X2}).");
        }

        var values = registers.Zip(first, (register, value) => new RegisterValue(register, value)).ToArray();
        return new PreMToolState(firmware.Version, firmware.Date, now, Array.AsReadOnly(values));
    }
}

/// <summary>
/// Stores the pre-M-Tool snapshot once; an existing file is never overwritten. Only a complete,
/// readable snapshot for the running firmware counts as saved.
/// </summary>
public sealed class PreStateStore(string directory)
{
    private const string FileName = "pre-mtool-state.json";

    private string FilePath => Path.Combine(directory, FileName);

    public bool Exists => File.Exists(FilePath);

    /// <summary>The saved snapshot, or null when there is none or it is unreadable/incomplete.</summary>
    public PreMToolState? Load()
    {
        if (!Exists)
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<PreMToolState>(File.ReadAllText(FilePath), JsonDefaults.Options);
            return IsComplete(state) ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool HasValidSnapshot(string firmwareVersion) => Load()?.FirmwareVersion == firmwareVersion;

    /// <returns>True when saved; false when a snapshot file already existed.</returns>
    public bool SaveIfMissing(PreMToolState state)
    {
        Directory.CreateDirectory(directory);
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonDefaults.Options));
        try
        {
            // Rename is atomic: the snapshot is either complete or absent, never half-written.
            File.Move(temporary, FilePath, overwrite: false);
            return true;
        }
        catch (IOException) when (Exists)
        {
            File.Delete(temporary);
            return false;
        }
    }

    /// <summary>
    /// Every writable register must be present. Extra registers are allowed: snapshots taken before
    /// stage 5-WMI also hold the down offsets, which are no longer writable.
    /// </summary>
    private static bool IsComplete(PreMToolState? state) =>
        state is { FirmwareVersion: not null, FirmwareDate: not null, Registers: not null }
        && state.Registers.All(r => r is not null)
        && state.Registers.Select(r => r.Register).ToHashSet() is var saved
        && saved.Count == state.Registers.Count
        && saved.IsSupersetOf(EcWriteRules.WritableRegisters);
}
