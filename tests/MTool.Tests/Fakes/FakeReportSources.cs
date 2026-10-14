using MTool.App.Cli;
using MTool.Core.Device;
using MTool.Core.Device.Config;
using MTool.Core.Diagnostics;

namespace MTool.Tests.Fakes;

/// <summary>The report's sources without a laptop; counts the calls that matter for safety.</summary>
internal sealed class FakeReportSources : IReportSources
{
    public static readonly byte[] DsdtBytes = [0x44, 0x53, 0x44, 0x54, 0x01, 0x02];

    public WmiInterface? Wmi { get; init; } = WmiInterface.Wmi1;

    public bool ProbeFails { get; init; }

    public bool DsdtFails { get; init; }

    public bool SystemFails { get; init; }

    public bool Wmi2Fails { get; init; }

    public string? Model { get; init; } = "P65 Creator 9SE";

    public string Windows { get; init; } = "Windows 11";

    public string? Firmware { get; init; } = "16Q4EMS2.107";

    /// <summary>When set, <see cref="ReadSystem"/> signals <see cref="Started"/> and waits for it: a report that is still running.</summary>
    public ManualResetEventSlim? Gate { get; init; }

    public ManualResetEventSlim Started { get; } = new();

    public int Wmi2Reads { get; private set; }

    public int EcReads { get; private set; }

    public int InterfaceReads { get; private set; }

    public int DsdtReads { get; private set; }

    public SystemInfo ReadSystem()
    {
        Started.Set();
        Gate?.Wait(TimeSpan.FromSeconds(10));
        return SystemFails
            ? throw new InvalidOperationException("WMI yok")
            : new SystemInfo("MSI", Model, "MS-16Q4", "E16Q4IMS.10D", "2019-05-13", "i7-9750H", Windows);
    }

    public WmiInterface? ReadInterface()
    {
        InterfaceReads++;
        return ProbeFails ? throw new TimeoutException("zaman aşımı") : Wmi;
    }

    public IReadOnlyList<WmiClassInfo> Classes { get; init; } = [new WmiClassInfo("MSI_CPU", ["CPU"])];

    public IReadOnlyList<WmiClassInfo> ReadClasses() => Classes;

    public byte[] ReadDsdt()
    {
        DsdtReads++;
        return DsdtFails ? throw new InvalidOperationException("tablo yok") : DsdtBytes;
    }

    public Wmi2Readout ReadWmi2()
    {
        Wmi2Reads++;
        if (Wmi2Fails)
        {
            throw new InvalidOperationException("no answer");
        }

        return new Wmi2Readout([new Wmi2Call("Get_EC", 0, new byte[32], null)]);
    }

    public EcReadout ReadEc()
    {
        EcReads++;
        return new EcReadout(Firmware is null ? null : new FirmwareInfo(Firmware, "05132019"), DeviceMatch.None,
            "msi-p65-creator-9se", TestLayouts.P65.Capabilities, new FieldScan([], 0));
    }
}
