using System.Text.Json;
using MTool.Core.Profiles;

namespace MTool.Core.Settings;

/// <param name="DryRun">When true the gateway validates and logs but never writes. Default: on.</param>
/// <param name="Desired">What the user wants the EC to hold; missing or null means <see cref="DesiredState.Default"/>.</param>
public sealed record AppSettings(
    int SchemaVersion = AppSettings.CurrentSchemaVersion,
    bool DryRun = true,
    DesiredState? Desired = null)
{
    public const int CurrentSchemaVersion = 1;

    public DesiredState Desired
    {
        get;
        init => field = value ?? DesiredState.Default;
    }

    = Desired ?? DesiredState.Default;

    public static AppSettings Default { get; } = new();
}

/// <param name="Warning">Set when the file was unusable and defaults were loaded instead.</param>
public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

/// <summary><c>settings.json</c> under the app folder. Corrupt files are set aside, never silently lost.</summary>
public sealed class SettingsStore(string directory)
{
    private const string FileName = "settings.json";

    private string FilePath => Path.Combine(directory, FileName);

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(AppSettings.Default, null);
        }

        try
        {
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
            return new SettingsLoadResult(AppSettings.Default, $"{FileName} okunamadı ({ex.Message}); varsayılanlar kullanılıyor.");
        }
    }

    /// <summary>Writes to a temporary file first so a crash never leaves a half-written settings file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonDefaults.Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private SettingsLoadResult SetAside(string reason)
    {
        var badPath = $"{FilePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        File.Move(FilePath, badPath, overwrite: false);
        return new SettingsLoadResult(
            AppSettings.Default,
            $"{FileName} okunamadı ({reason}); {Path.GetFileName(badPath)} olarak saklandı, varsayılanlar kullanılıyor.");
    }
}
