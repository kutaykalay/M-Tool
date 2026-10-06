using System.Collections.Frozen;
using MTool.Core.Device.Config;

namespace MTool.Core.Device;

/// <summary>
/// Which WMI1 field reads each EC register of one model, and which registers go through the port
/// instead. Built from a validated <see cref="DeviceConfig"/>. Frozen: a cast back to a mutable
/// collection must not be able to widen what the router reaches.
/// </summary>
public sealed class WmiFieldMap
{
    private WmiFieldMap(FrozenDictionary<byte, WmiField> fields, FrozenSet<byte> portRegisters) =>
        (Fields, FieldSet, PortRegisters) = (fields, fields.Values.ToFrozenSet(), portRegisters);

    public IReadOnlyDictionary<byte, WmiField> Fields { get; }

    /// <summary>Every mapped (class, index) pair, for checking a WMI path without a lookup by register.</summary>
    public IReadOnlySet<WmiField> FieldSet { get; }

    public IReadOnlySet<byte> PortRegisters { get; }

    /// <param name="config">
    /// Already validated (<see cref="DeviceLayout.From"/> checks it): only the validator rejects a
    /// register listed twice; nothing here checks it again.
    /// </param>
    internal static WmiFieldMap From(DeviceConfig config, Wmi1Layout wmi) => new(
        wmi.Fields.ToFrozenDictionary(f => f.Register, f => new WmiField(f.Class, f.Index)),
        config.PortRegisters.ToFrozenSet());
}
