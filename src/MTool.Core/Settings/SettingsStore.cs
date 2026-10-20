using System.Globalization;
using System.Text;
using System.Text.Json;
using MTool.Core.Localization;
using MTool.Core.Power;
using MTool.Core.Profiles;
using MTool.Core.Resources;

namespace MTool.Core.Settings;

/// <param name="DryRun">When true the gateway validates and logs but never writes. Default: on.</param>
/// <param name="Desired">What the user wants the EC to hold; missing or null means <see cref="DesiredState.Default"/>.</param>
/// <param name="CustomProfiles">
/// The user's own fan profiles in creation order; missing or null means none. Outside input like the
/// rest of the file: <see cref="SettingsSanitizer"/> checks them before they reach the catalog.
/// </param>
/// <param name="PowerSwitch">
/// Separate choices on AC and on battery; missing or null means off. Added without a new schema
/// version: an older M-Tool loads the file and only drops this field when it saves.
/// </param>
/// <param name="Language">
/// The interface language the user chose, spelled as in <see cref="Localization.SupportedLanguages"/>;
/// null means follow Windows. Outside input: <see cref="SettingsSanitizer"/> checks it. Added without a
/// new schema version, like <paramref name="PowerSwitch"/>.
/// </param>
/// <param name="AutoUpdateCheck">
/// Ask GitHub for the newest release once at start-up. Off unless the user turns it on: M-Tool goes to
/// the network only by a user's choice. Added without a new schema version; missing means off.
/// </param>
public sealed record AppSettings(
    int SchemaVersion = AppSettings.CurrentSchemaVersion,
    bool DryRun = true,
    DesiredState? Desired = null,
    IReadOnlyList<FanProfile>? CustomProfiles = null,
    PowerSwitchSettings? PowerSwitch = null,
    string? Language = null,
    bool AutoUpdateCheck = false)
{
    public const int CurrentSchemaVersion = 1;

    public DesiredState Desired
    {
        get;
        init => field = value ?? DesiredState.Default;
    }

    = Desired ?? DesiredState.Default;

    public IReadOnlyList<FanProfile> CustomProfiles
    {
        get;
        init => field = value ?? [];
    }

    = CustomProfiles ?? [];

    public PowerSwitchSettings PowerSwitch
    {
        get;
        init => field = value ?? PowerSwitchSettings.Default;
    }

    = PowerSwitch ?? PowerSwitchSettings.Default;

    public static AppSettings Default { get; } = new();

    /// <summary>Custom profiles by content, not by list reference: loaded settings equal the saved ones.</summary>
    public bool Equals(AppSettings? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && DryRun == other.DryRun
        && Desired == other.Desired
        && CustomProfiles.SequenceEqual(other.CustomProfiles)
        && PowerSwitch == other.PowerSwitch
        && Language == other.Language
        && AutoUpdateCheck == other.AutoUpdateCheck;

    public override int GetHashCode() =>
        HashCode.Combine(SchemaVersion, DryRun, Desired, CustomProfiles.Count, PowerSwitch, Language, AutoUpdateCheck);
}

/// <param name="Warning">Set when the file was unusable and defaults were loaded instead. For the user, in the UI language of that moment.</param>
/// <param name="LogWarning">The same warning for the log, always in English; null when <paramref name="Warning"/> is.</param>
/// <remarks>
/// A warning always comes with the default settings, which have no language field, so the app then follows
/// Windows: the language the warning is written in before the app has set its own is the one it will pick.
/// </remarks>
public sealed record SettingsLoadResult(AppSettings Settings, string? Warning, string? LogWarning = null);

/// <summary>A message for the user and its copy for the log (always English).</summary>
public sealed record SettingsNotice(string Text, string Log);

/// <summary><c>settings.json</c> under the app folder. Corrupt files are set aside, never silently lost.</summary>
public sealed class SettingsStore(string directory)
{
    /// <summary>
    /// A real settings file is a few kilobytes. Anything larger is set aside unread, so a planted file
    /// cannot slow start-up, flood the log with warnings or be copied over and over.
    /// </summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>
    /// Set-aside copies kept; older ones are deleted, so a file that keeps breaking cannot fill the
    /// folder. Ten bad starts in a row is far beyond what a user would let pass unnoticed.
    /// </summary>
    public const int MaxKeptCopies = 10;

    private const string FileName = "settings.json";

    private string FilePath => Path.Combine(directory, FileName);

    /// <summary>
    /// Set at load when the file is kept but this session must not save over it: it could not be read
    /// (locked, no permission), or a newer M-Tool wrote it. Saving would replace the user's custom
    /// profiles with the defaults this session started on. Save words the reason when it is asked to
    /// save, in the language the user reads then, not the one at load.
    /// </summary>
    private volatile SaveRefusal _saveRefusal;

    private enum SaveRefusal
    {
        None,
        Unread,
        NewerVersion,
    }

    private static string RefusalText(SaveRefusal refusal) => CoreText.Get(
        refusal == SaveRefusal.Unread ? nameof(CoreStrings.Store_RefuseUnread) : nameof(CoreStrings.Store_RefuseNewer),
        CultureInfo.CurrentUICulture);

    /// <summary>Each load decides afresh whether this session may save.</summary>
    public SettingsLoadResult Load()
    {
        _saveRefusal = SaveRefusal.None;
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(AppSettings.Default, null);
        }

        try
        {
            if (ReadAtMost(MaxFileBytes) is not { } content)
            {
                return SetAside(c => CoreText.Get(nameof(CoreStrings.Store_ReasonTooBig), c, MaxFileBytes / 1024));
            }

            return JsonSerializer.Deserialize<AppSettings>(WithoutByteOrderMark(content), JsonDefaults.Options) switch
            {
                null => SetAside(c => CoreText.Get(nameof(CoreStrings.Store_ReasonEmpty), c)),
                { SchemaVersion: < 1 } settings => SetAside(c => CoreText.Get(nameof(CoreStrings.Store_ReasonVersion), c, settings.SchemaVersion)),
                { SchemaVersion: > AppSettings.CurrentSchemaVersion } => FromNewerVersion(),
                var settings => new SettingsLoadResult(settings, null),
            };
        }
        catch (JsonException ex)
        {
            return SetAside(_ => ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable (locked, no permission): leave the file alone and run on safe defaults.
            _saveRefusal = SaveRefusal.Unread;
            var (text, log) = CoreText.ForUserAndLog(c => CoreText.Get(nameof(CoreStrings.Store_Unreadable), c, ex.Message));
            return new SettingsLoadResult(AppSettings.Default, text, log);
        }
    }

    /// <summary>Writes to a temporary file first so a crash never leaves a half-written settings file.</summary>
    /// <exception cref="IOException">
    /// The file could not be read at load, or a newer M-Tool wrote it; it is kept as it is.
    /// </exception>
    public void Save(AppSettings settings)
    {
        if (_saveRefusal is not SaveRefusal.None and var refusal)
        {
            throw new IOException(RefusalText(refusal));
        }

        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonDefaults.Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>
    /// Copies the file aside before the app saves over it, e.g. after custom profiles were dropped at load.
    /// An identical copy kept earlier is reused, so starting again without a save adds no new file.
    /// Returns a message for the user, or null when there is no file to copy.
    /// </summary>
    public string? PreserveCopy() => PreserveCopyNotice()?.Text;

    /// <summary><see cref="PreserveCopy"/> with the English copy of the message for the log.</summary>
    public SettingsNotice? PreserveCopyNotice()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllBytes(FilePath);
            var copyPath = ExistingCopyOf(content) ?? BadPath();
            if (!File.Exists(copyPath))
            {
                // CreateNew: never overwrite or follow something already at the new name.
                using (var copy = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write))
                {
                    copy.Write(content);
                }

                DeleteOldCopies(kept: copyPath);
            }

            var name = Path.GetFileName(copyPath);
            return Notice(c => CoreText.Get(nameof(CoreStrings.Store_CopyKept), c, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Notice(c => CoreText.Get(nameof(CoreStrings.Store_CopyFailed), c, ex.Message));
        }
    }

    private string? ExistingCopyOf(byte[] content) =>
        Directory.EnumerateFiles(directory, $"{FileName}.bad-*")
            .FirstOrDefault(path => new FileInfo(path).Length == content.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(content));

    /// <summary>The time stamp leads the name, so name order is age order.</summary>
    private string BadPath() => $"{FilePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";

    /// <summary>
    /// One open handle from the size check to the last byte, so the file cannot grow in between.
    /// Null when it is larger than <paramref name="limit"/>; at most one byte past it is read.
    /// </summary>
    private byte[]? ReadAtMost(int limit)
    {
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[limit + 1];
        var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return length > limit ? null : buffer[..length];
    }

    /// <summary>Notepad may save with one; the JSON reader does not expect it.</summary>
    private static ReadOnlySpan<byte> WithoutByteOrderMark(byte[] content) =>
        content.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? content.AsSpan(Encoding.UTF8.Preamble.Length) : content;

    /// <summary>
    /// Its fields may mean something this version does not know, and saving would drop the ones it
    /// does not read. So run on safe defaults and leave the file to the version that wrote it.
    /// </summary>
    private SettingsLoadResult FromNewerVersion()
    {
        _saveRefusal = SaveRefusal.NewerVersion;
        var (text, log) = CoreText.ForUserAndLog(c => CoreText.Get(nameof(CoreStrings.Store_NewerVersion), c));
        return new SettingsLoadResult(AppSettings.Default, text, log);
    }

    /// <summary>
    /// Keeps the newest <see cref="MaxKeptCopies"/>. Best effort: called after a copy was kept, which
    /// must still be reported as kept, and a copy left behind now is deleted on the next try.
    /// </summary>
    /// <param name="kept">The copy just made; never deleted, even when a clock set back made its name sort oldest.</param>
    private void DeleteOldCopies(string kept)
    {
        try
        {
            var others = Directory.GetFiles(directory, $"{FileName}.bad-*")
                .Where(path => !string.Equals(Path.GetFileName(path), Path.GetFileName(kept), StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (var old in others.Take(others.Length - (MaxKeptCopies - 1)))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to tell the user: the new copy is safe, only the folder is untidier.
        }
    }

    /// <summary>Also called from a catch block in <see cref="Load"/>, so it handles its own failure.</summary>
    /// <param name="reason">The reason in the language it is asked for: the warning is made for the user and for the log.</param>
    private SettingsLoadResult SetAside(Func<CultureInfo, string> reason)
    {
        var badPath = BadPath();
        try
        {
            File.Move(FilePath, badPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held by another process: leave it where it is and run on safe defaults. Saving would
            // replace it while no copy of it exists.
            _saveRefusal = SaveRefusal.Unread;
            var (blockedText, blockedLog) = CoreText.ForUserAndLog(
                c => CoreText.Get(nameof(CoreStrings.Store_UnreadableNotMoved), c, reason(c), ex.Message));
            return new SettingsLoadResult(AppSettings.Default, blockedText, blockedLog);
        }

        DeleteOldCopies(kept: badPath);
        var kept = Path.GetFileName(badPath);
        var (text, log) = CoreText.ForUserAndLog(c => CoreText.Get(nameof(CoreStrings.Store_SetAside), c, reason(c), kept));
        return new SettingsLoadResult(AppSettings.Default, text, log);
    }

    private static SettingsNotice Notice(Func<CultureInfo, string> make)
    {
        var (text, log) = CoreText.ForUserAndLog(make);
        return new SettingsNotice(text, log);
    }
}
