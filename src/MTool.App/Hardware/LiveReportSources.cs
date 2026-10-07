using System.Management;
using MTool.App.Cli;
using MTool.Core;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.App.Hardware;

/// <summary>
/// The report's real sources. SMBIOS is queried field by field (never <c>SELECT *</c>), so a serial
/// number or UUID cannot slip in. The EC session is WMI only: PawnIO is never opened.
/// </summary>
internal sealed class LiveReportSources(IAppLog log) : IReportSources
{
    private const string Cimv2 = @"root\cimv2";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>One WMI1 call (2 s) plus the EC lock (0.5 s), with room to spare.</summary>
    private static readonly TimeSpan FieldTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Each query on its own: a slow one loses its own fields only.</summary>
    public SystemInfo ReadSystem()
    {
        var computer = Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer", "Model");
        var board = Query("SELECT Product FROM Win32_BaseBoard", "Product");
        var bios = Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", "SMBIOSBIOSVersion", "ReleaseDate");
        var cpu = Query("SELECT Name FROM Win32_Processor", "Name");
        var os = Query("SELECT Caption, Version FROM Win32_OperatingSystem", "Caption", "Version");
        var windows = string.Join(' ', new[] { os[0], os[1] }.OfType<string>());
        return new SystemInfo(computer[0], computer[1], board[0], bios[0], Date(bios[1]), cpu[0], windows.Length > 0 ? windows : null);
    }

    public WmiInterface? ReadInterface() => WmiProbe.Detect();

    public IReadOnlyList<WmiClassInfo> ReadClasses() => WmiProbe.MsiClasses();

    public byte[] ReadDsdt() => FirmwareTables.ReadDsdt();

    /// <summary>Checks the interface again itself: the WMI2 methods must never run on a WMI1 laptop.</summary>
    public Wmi2Readout ReadWmi2() => WmiProbe.Detect() == WmiInterface.Wmi2
        ? MsiWmi2Methods.Probe()
        : throw new InvalidOperationException("WMI2 arayüzü yok; WMI2 yöntemleri çağrılmadı.");

    public EcReadout ReadEc()
    {
        using var session = EcSession.Open(log, EcBackends.WmiOnly);
        var layout = session.Layout;
        var firmware = ReadFirmware(session, layout);
        var fields = FieldScan.Run(
            [.. layout.Wmi.Fields.OrderBy(f => f.Key).Select(f => (f.Key, $"{f.Value.ClassName}[{f.Value.Index}]"))],
            register => TryRead(session, register));
        // The record's capabilities, not this WMI-only session's: the report describes the model.
        var record = session.Catalog.First(c => c.Id == layout.Id);
        return new EcReadout(firmware, session.Match(firmware), layout.Id, DeviceCapabilities.From(record), fields);
    }

    private FirmwareInfo? ReadFirmware(EcSession session, DeviceLayout layout)
    {
        try
        {
            return CliRunner.Wait(session.Worker.RunAsync(ec => new P65Device(ec, layout).ReadFirmware()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"Report: firmware unreadable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// One worker call per field with its own timeout: a field that hangs costs only this wait. A
    /// hung call keeps the worker busy, so the next ones time out too and the scan stops.
    /// </summary>
    private static byte? TryRead(EcSession session, byte register)
    {
        try
        {
            return session.Worker.RunAsync(ec => ec.Read(register)).WaitAsync(FieldTimeout).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <returns>The properties' text values in order; null for any that could not be read.</returns>
    private string?[] Query(string query, params string[] properties)
    {
        try
        {
            return MsiWmiFields.Bounded("sistem bilgisi", QueryTimeout, () =>
            {
                var scope = new ManagementScope(Cimv2, new ConnectionOptions { Timeout = QueryTimeout });
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query), new EnumerationOptions { Timeout = QueryTimeout });
                using var results = searcher.Get();
                var values = new string?[properties.Length];
                foreach (var item in results.Cast<ManagementBaseObject>())
                {
                    using (item)
                    {
                        // The first instance only (one board, one BIOS, the first CPU), but every one is disposed.
                        if (Array.TrueForAll(values, v => v is null))
                        {
                            values = [.. properties.Select(p => item[p]?.ToString()?.Trim() is { Length: > 0 } text ? text : null)];
                        }
                    }
                }

                return values;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Warn($"Report: \"{query}\" unreadable: {ex.Message}");
            return new string?[properties.Length];
        }
    }

    /// <summary>WMI dates look like <c>20190513000000.000000+000</c>.</summary>
    private static string? Date(string? wmiDate) =>
        wmiDate is { Length: >= 8 } d ? $"{d[..4]}-{d[4..6]}-{d[6..8]}" : wmiDate;
}
