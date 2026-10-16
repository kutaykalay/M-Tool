using MTool.Core.Device;

namespace MTool.App.ViewModels;

/// <summary>
/// The live values of a WMI2 laptop, read only (<c>Get_EC</c> and <c>Get_Data</c> at the pinned
/// registers). Every read goes to the laptop; disposing closes the interface.
/// </summary>
internal interface IWmi2LiveSource : IDisposable
{
    /// <summary>The EC firmware version, or null when the answer has none.</summary>
    Task<string?> ReadFirmwareAsync(CancellationToken cancellationToken);

    Task<Wmi2Status> ReadStatusAsync(CancellationToken cancellationToken);

    Task<SensorSnapshot> ReadSensorsAsync(CancellationToken cancellationToken);
}
