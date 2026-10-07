using System.Management;
using MTool.Core;
using MTool.Core.Diagnostics;
using MTool.Core.Device.Config;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>A model M-Tool cannot read yet; the message is meant for the user.</summary>
internal sealed class UnsupportedDeviceException(string message) : Exception(message);

/// <summary>
/// Finds which MSI WMI interface this laptop has before a session opens, so a model M-Tool cannot
/// read gets a clear message instead of a WMI error. Only class definitions are queried: no
/// instance is read, no method is called, the EC is never touched.
/// </summary>
internal static class WmiProbe
{
    private const string Wmi1Class = "MSI_Software";
    private const string Wmi2Class = "MSI_ACPI";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <exception cref="EcAccessException">WMI does not answer.</exception>
    public static WmiInterface? Detect() => MsiWmiFields.Bounded("arayüz algılama", Timeout, () =>
    {
        var scope = MsiWmiFields.Connect();
        return Classify(HasClass(scope, Wmi1Class), HasClass(scope, Wmi2Class));
    });

    /// <summary>
    /// Every MSI class in <c>root\WMI</c> with its property names, from the class definitions only:
    /// listing instances would run the firmware's ACPI methods, which a report must not do.
    /// </summary>
    /// <exception cref="EcAccessException">WMI does not answer.</exception>
    public static IReadOnlyList<WmiClassInfo> MsiClasses() => MsiWmiFields.Bounded("sınıf listesi", Timeout, () =>
    {
        using var searcher = new ManagementObjectSearcher(
            MsiWmiFields.Connect(), new ObjectQuery("SELECT * FROM meta_class WHERE __CLASS LIKE 'MSI[_]%'"),
            new EnumerationOptions { Timeout = Timeout });
        using var classes = searcher.Get();
        var found = new List<WmiClassInfo>();
        foreach (var item in classes.Cast<ManagementClass>())
        {
            using (item)
            {
                found.Add(new WmiClassInfo(
                    item.ClassPath.ClassName,
                    Array.AsReadOnly([.. item.Properties.Cast<PropertyData>().Select(p => p.Name).Order(StringComparer.Ordinal)])));
            }
        }

        return (IReadOnlyList<WmiClassInfo>)found.OrderBy(c => c.Name, StringComparer.Ordinal).ToList().AsReadOnly();
    });

    /// <summary>WMI1 wins when both exist: it is the interface M-Tool was verified on.</summary>
    internal static WmiInterface? Classify(bool hasSoftware, bool hasAcpi) =>
        hasSoftware ? WmiInterface.Wmi1 : hasAcpi ? WmiInterface.Wmi2 : null;

    internal static string UnsupportedMessage(WmiInterface? detected) => detected == WmiInterface.Wmi2
        ? "Bu MSI modeli henüz desteklenmiyor: EC'ye MSI'ın WMI2 arayüzüyle erişiliyor (çoğunlukla 11. nesil Intel ve sonrası). " +
          "M-Tool şimdilik yalnızca WMI1 modellerini okuyabiliyor. Desteğe katkı için yönetici komut isteminde " +
          "\"M-Tool.exe --report --wmi2\" çalıştırıp oluşan zip'i GitHub'da \"Device report\" issue'suna ekleyebilirsiniz."
        : $"Bu bilgisayar henüz desteklenmiyor: MSI WMI arayüzü (root\\WMI içinde {Wmi1Class} ya da {Wmi2Class}) bulunamadı.";

    private static bool HasClass(ManagementScope scope, string className)
    {
        using var searcher = new ManagementObjectSearcher(
            scope, new ObjectQuery($"SELECT * FROM meta_class WHERE __CLASS = '{className}'"), new EnumerationOptions { Timeout = Timeout });
        using var classes = searcher.Get();
        return classes.Count > 0;
    }
}

/// <summary>
/// The check before a session opens. Only a definite answer stops start-up: a probe that times out
/// or fails (WMI still starting at log-on) is logged and the session opens as it did before the
/// probe existed, failing there if WMI1 really is missing. A confirmed WMI1 is not probed again.
/// </summary>
internal sealed class WmiInterfaceCheck(Func<WmiInterface?> detect)
{
    private volatile bool _wmi1Confirmed;

    public static WmiInterfaceCheck Default { get; } = new(WmiProbe.Detect);

    /// <exception cref="UnsupportedDeviceException">The probe answered: WMI2 only, or no MSI WMI at all.</exception>
    public void EnsureWmi1(IAppLog log)
    {
        if (_wmi1Confirmed)
        {
            return;
        }

        WmiInterface? detected;
        try
        {
            detected = detect();
        }
        catch (EcAccessException ex)
        {
            log.Warn($"MSI WMI interface not detected, trying to open the session anyway: {ex.Message}");
            return;
        }

        if (detected != WmiInterface.Wmi1)
        {
            throw new UnsupportedDeviceException(WmiProbe.UnsupportedMessage(detected));
        }

        _wmi1Confirmed = true;
    }
}
