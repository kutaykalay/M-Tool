using System.Text.Json;
using MTool.Core.Profiles;

namespace MTool.Core.Settings;

/// <param name="DryRun">When true the gateway validates and logs but never writes. Default: on.</param>
/// <param name="Desired">What the user wants the EC to hold; missing or null means <see cref="DesiredState.Default"/>.</param>
/// <param name="CustomProfiles">
/// The user's own fan profiles in creation order; missing or null means none. Outside input like the
/// rest of the file: <see cref="SettingsSanitizer"/> checks them before they reach the catalog.
/// </param>
public sealed record AppSettings(
    int SchemaVersion = AppSettings.CurrentSchemaVersion,
    bool DryRun = true,
    DesiredState? Desired = null,
    IReadOnlyList<FanProfile>? CustomProfiles = null)
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

    public static AppSettings Default { get; } = new();

    /// <summary>Custom profiles by content, not by list reference: loaded settings equal the saved ones.</summary>
    public bool Equals(AppSettings? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && DryRun == other.DryRun
        && Desired == other.Desired
        && CustomProfiles.SequenceEqual(other.CustomProfiles);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, DryRun, Desired, CustomProfiles.Count);
}

/// <param name="Warning">Set when the file was unusable and defaults were loaded instead.</param>
public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

/// <summary><c>settings.json</c> under the app folder. Corrupt files are set aside, never silently lost.</summary>
public sealed class SettingsStore(string directory)
{
    /// <summary>
    /// A real settings file is a few kilobytes. Anything larger is set aside unread, so a planted file
    /// cannot slow start-up, flood the log with warnings or be copied over and over.
    /// </summary>
    public const int MaxFileBytes = 1024 * 1024;

    private const string FileName = "settings.json";

    private string FilePath => Path.Combine(directory, FileName);

    /// <summary>
    /// Set when the file existed but could not be read (locked, no permission). Saving over it would
    /// replace the user's custom profiles with the defaults this session started on.
    /// </summary>
    private volatile bool _unreadAtLoad;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(AppSettings.Default, null);
        }

        try
        {
            if (new FileInfo(FilePath).Length > MaxFileBytes)
            {
                return SetAside($"{MaxFileBytes / 1024} KB'tan büyük");
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonDefaults.Options);
            return settings is not null
                ? new SettingsLoadResult(settings, null)
                : SetAside("boş ya da eksik");
        }
        catch (JsonException ex)
        {
            return SetAside(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable (locked, no permission): leave the file alone and run on safe defaults.
            _unreadAtLoad = true;
            return new SettingsLoadResult(AppSettings.Default, $"{FileName} okunamadı ({ex.Message}); varsayılanlar kullanılıyor.");
        }
    }

    /// <summary>Writes to a temporary file first so a crash never leaves a half-written settings file.</summary>
    /// <exception cref="IOException">The file could not be read at load; it is kept as it is.</exception>
    public void Save(AppSettings settings)
    {
        if (_unreadAtLoad)
        {
            throw new IOException($"{FileName} açılışta okunamadığı için üzerine yazılmıyor; M-Tool'u yeniden başlatın");
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
    public string? PreserveCopy()
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
                using var copy = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write);
                copy.Write(content);
            }

            return $"{FileName} dosyasının önceki hali {Path.GetFileName(copyPath)} olarak saklandı.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{FileName} dosyasının kopyası saklanamadı ({ex.Message}); kenara ayrılan profiller sonraki kaydetmede kaybolur.";
        }
    }

    private string? ExistingCopyOf(byte[] content) =>
        Directory.EnumerateFiles(directory, $"{FileName}.bad-*")
            .FirstOrDefault(path => new FileInfo(path).Length == content.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(content));

    private string BadPath() => $"{FilePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";

    /// <summary>Also called from a catch block in <see cref="Load"/>, so it handles its own failure.</summary>
    private SettingsLoadResult SetAside(string reason)
    {
        var badPath = BadPath();
        try
        {
            File.Move(FilePath, badPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held by another process: leave it where it is and run on safe defaults.
            return new SettingsLoadResult(
                AppSettings.Default,
                $"{FileName} okunamadı ({reason}) ve kenara alınamadı ({ex.Message}); varsayılanlar kullanılıyor.");
        }

        return new SettingsLoadResult(
            AppSettings.Default,
            $"{FileName} okunamadı ({reason}); {Path.GetFileName(badPath)} olarak saklandı, varsayılanlar kullanılıyor.");
    }
}
