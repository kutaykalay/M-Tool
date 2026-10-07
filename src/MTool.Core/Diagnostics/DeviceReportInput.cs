using MTool.Core.Device;
using MTool.Core.Device.Config;

namespace MTool.Core.Diagnostics;

/// <summary>
/// One part of the report: its value, or why it could not be read. A failed part never stops the
/// report. Made only through <see cref="Section.Ok{T}"/> and <see cref="Section.Failed{T}"/>, so it
/// is always exactly one of the two.
/// </summary>
public sealed record Section<T>
{
    internal Section(T? value, string? error) => (Value, Error) = (value, error);

    public T? Value { get; }

    public string? Error { get; }

    public bool IsOk => Error is null;
}

public static class Section
{
    public static Section<T> Ok<T>(T value) => new(value, null);

    public static Section<T> Failed<T>(string error) => new(default, string.IsNullOrWhiteSpace(error) ? "unknown error" : error);
}

/// <summary>
/// What SMBIOS says about the machine. Only these fields are ever queried: no serial number,
/// UUID, user or machine name. A field that could not be read is null.
/// </summary>
public sealed record SystemInfo(
    string? Manufacturer, string? Model, string? BaseBoard, string? BiosVersion, string? BiosDate, string? Cpu, string? Windows);

/// <summary>An MSI class in <c>root\WMI</c>, from its definition only (no instance read).</summary>
public sealed record WmiClassInfo(string Name, IReadOnlyList<string> Properties);

/// <param name="Value">Null when the read failed.</param>
public sealed record FieldReadout(byte Register, string Field, byte? Value);

/// <summary>What M-Tool read from the EC through WMI1; the raw port is never opened for a report.</summary>
/// <param name="LayoutId">The device record whose WMI map was used to read.</param>
/// <param name="Fields">The fields read, in register order; see <see cref="FieldScan"/>.</param>
public sealed record EcReadout(
    FirmwareInfo? Firmware, DeviceMatch Match, string LayoutId, DeviceCapabilities Capabilities, FieldScan Fields);

/// <param name="Computer">SMBIOS and Windows.</param>
public sealed record DeviceReportInput(
    string AppVersion,
    DateTimeOffset At,
    Section<SystemInfo> Computer,
    Section<WmiInterface?> Interface,
    Section<IReadOnlyList<WmiClassInfo>> Classes,
    Section<int> DsdtBytes,
    Section<EcReadout> Ec);
