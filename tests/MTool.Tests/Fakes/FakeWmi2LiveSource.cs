using MTool.App.ViewModels;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

/// <summary>A scripted WMI2 live source: counts each read, and fails the next reads on request. Never touches WMI.</summary>
internal sealed class FakeWmi2LiveSource : IWmi2LiveSource
{
    public string? Firmware { get; set; } = "16Q4EMS2.107";

    public Wmi2Status Status { get; set; } = new(0x00, 0xC1, 0x0D, 0xD0);

    public SensorSnapshot Sensors { get; set; } = new(60, 46, 45, 30, 2987, 0, new Wmi2Tach(0, 0xA0, 0, 0));

    public int FirmwareReads { get; private set; }

    public int StatusReads { get; private set; }

    public int SensorReads { get; private set; }

    public int Reads => FirmwareReads + StatusReads + SensorReads;

    /// <summary>Thrown by every read while set.</summary>
    public Exception? Error { get; set; }

    public bool Disposed { get; private set; }

    public Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken)
    {
        FirmwareReads++;
        return Answer(Firmware);
    }

    /// <summary>When set, the status read waits for it, like a call that takes long.</summary>
    public TaskCompletionSource? HoldStatus { get; set; }

    public async Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken)
    {
        StatusReads++;
        if (HoldStatus is { } hold)
        {
            await hold.Task;
        }

        return await Answer(Status);
    }

    public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken)
    {
        SensorReads++;
        return Answer(Sensors);
    }

    public void Dispose() => Disposed = true;

    private Task<T> Answer<T>(T value) => Error is { } error ? Task.FromException<T>(error) : Task.FromResult(value);

    public static EcAccessException Timeout(string message = "MSI_ACPI Get_Data(0x68) did not answer within 2 s") => new(message);
}
