using MTool.Core.Settings;

namespace MTool.App.ViewModels;

/// <summary>Where the user's yes to experimental WMI2 control is kept: <c>settings.json</c> in the app.</summary>
internal interface IWmi2Consent
{
    /// <summary>The consent as last loaded or saved; null when experimental control is off.</summary>
    Wmi2ExperimentalConsent? Current { get; }

    /// <summary>Saves the consent, or null to turn experimental control off; <see cref="Current"/> changes only when it was saved.</summary>
    /// <exception cref="IOException">The file could not be written, or this session may not save over it.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or folder may not be written.</exception>
    void Save(Wmi2ExperimentalConsent? consent);
}

/// <summary>
/// The consent in <c>settings.json</c>, for the WMI2 window: the rest of the settings are saved as they were
/// loaded (sanitized), the consent alone changes. Only for that window: a WMI2 laptop never opens the P65
/// window in the same run, so no other saver (ProfileService) holds a copy that this one could overwrite.
/// </summary>
internal sealed class SettingsWmi2Consent(SettingsStore store, AppSettings loaded) : IWmi2Consent
{
    private AppSettings _settings = loaded;

    public Wmi2ExperimentalConsent? Current => _settings.ExperimentalWmi2;

    public void Save(Wmi2ExperimentalConsent? consent)
    {
        var changed = _settings with { ExperimentalWmi2 = consent };
        store.Save(changed);
        _settings = changed;
    }
}
