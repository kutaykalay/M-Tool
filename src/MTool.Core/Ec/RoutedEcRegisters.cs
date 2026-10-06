using MTool.Core.Device;

namespace MTool.Core.Ec;

/// <summary>
/// The EC as M-Tool reaches it: every register in the session's <see cref="WmiFieldMap"/> through
/// MSI's WMI1, which goes through Windows' own EC driver and so never races it; only the map's port
/// registers (Cooler Boost, the charge limit) through the raw port protocol, and only when a port is
/// given. Any other register is refused before anything is touched, so the port can never be used for
/// polling or for a register Windows might be using. Writes stay on the verified P65 map
/// (<see cref="WmiMap"/>) and <see cref="EcWriteRules.WritableRegisters"/>, a second line behind
/// <see cref="EcGateway"/>, because WMI itself stores any value in any field; a register the session
/// map routes differently is never written. Not thread-safe: use it only from <see cref="EcWorker"/>.
/// A block costs one <see cref="IWmiFields.Read"/> per class, but <see cref="RetryingEcReader"/>
/// splits blocks into single reads; that is fine, as the WMI backend reads one field per call anyway.
/// </summary>
public sealed class RoutedEcRegisters : IEcRegisters, IEcWritableRegisters
{
    private const int RegisterCount = 256;

    private readonly IWmiFields _wmi;
    private readonly IEcWritableRegisters? _port;
    private readonly WmiFieldMap _map;

    /// <param name="port">Null runs WMI only: Cooler Boost and the charge limit are then refused too.</param>
    /// <param name="map">How this model's registers are read.</param>
    internal RoutedEcRegisters(IWmiFields wmi, IEcWritableRegisters? port, WmiFieldMap map)
    {
        _wmi = wmi;
        _port = port;
        _map = map;
    }

    /// <param name="port">Null runs WMI only: Cooler Boost and the charge limit are then refused too.</param>
    /// <param name="map">How this model's registers are read.</param>
    public static RoutedEcRegisters Create(IWmiFields wmi, EcController? port, WmiFieldMap map) => new(wmi, port, map);

    public byte Read(byte register) => ReadBlock(register, 1)[0];

    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startRegister + count, RegisterCount, nameof(count));

        var registers = Enumerable.Range(startRegister, count).Select(r => (byte)r).ToArray();
        foreach (var register in registers)
        {
            EnsureRoutable(register);
        }

        var values = new Dictionary<byte, byte>();
        foreach (var group in registers.Where(_map.Fields.ContainsKey).GroupBy(r => _map.Fields[r].ClassName))
        {
            ReadWmiClass(group.Key, group.ToArray(), values);
        }

        foreach (var register in registers.Where(r => !values.ContainsKey(r)))
        {
            values[register] = _port!.Read(register);
        }

        return Array.AsReadOnly(registers.Select(r => values[r]).ToArray());
    }

    void IEcWritableRegisters.Write(byte register, byte value)
    {
        EnsureRoutable(register);
        if (!EcWriteRules.WritableRegisters.Contains(register))
        {
            throw new InvalidOperationException($"0x{register:X2} yazılabilir register listesinde değil.");
        }

        if (!RoutesLikeVerifiedMap(register))
        {
            throw new InvalidOperationException($"0x{register:X2}: oturum haritası bu register'ı doğrulanmış P65 haritasından farklı yönlendiriyor; yazılmaz.");
        }

        if (WmiMap.Fields.TryGetValue(register, out var field))
        {
            _wmi.Write(field.ClassName, field.Index, value);
            return;
        }

        _port!.Write(register, value);
    }

    private void ReadWmiClass(string className, IReadOnlyList<byte> registers, Dictionary<byte, byte> values)
    {
        var fields = registers.Select(r => _map.Fields[r]).ToArray();
        var read = _wmi.Read(className, fields.Select(f => f.Index).ToArray());
        if (read.Count != fields.Length)
        {
            throw new EcAccessException($"WMI {className}: {fields.Length} alan istendi, {read.Count} geldi.");
        }

        for (var i = 0; i < fields.Length; i++)
        {
            values[registers[i]] = read[i] is >= byte.MinValue and <= byte.MaxValue
                ? (byte)read[i]
                : throw new EcAccessException($"WMI {fields[i]} bayt sınırı dışında: {read[i]}.");
        }
    }

    /// <summary>The same WMI field, or the port on both maps: then the write goes where it was verified.</summary>
    private bool RoutesLikeVerifiedMap(byte register) =>
        _map.Fields.TryGetValue(register, out var session)
            ? WmiMap.Fields.TryGetValue(register, out var verified) && session == verified
            : _map.PortRegisters.Contains(register) && WmiMap.PortRegisters.Contains(register) && !WmiMap.Fields.ContainsKey(register);

    /// <summary>Programming errors, not EC trouble: never retried, never sent anywhere.</summary>
    private void EnsureRoutable(byte register)
    {
        if (_map.Fields.ContainsKey(register))
        {
            return;
        }

        // Both maps must allow the port: a session map can never add a port register beyond 0x98 and 0xEF.
        if (!_map.PortRegisters.Contains(register) || !WmiMap.PortRegisters.Contains(register))
        {
            throw new InvalidOperationException($"0x{register:X2} WMI haritasında yok ve port için izinli değil.");
        }

        if (_port is null)
        {
            throw new InvalidOperationException($"0x{register:X2} yalnızca port üzerinden erişilebilir; port kapalı.");
        }
    }
}
