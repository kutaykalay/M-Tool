using MTool.Core.Device;

namespace MTool.Core.Ec;

/// <summary>
/// The EC as M-Tool reaches it: every register in <see cref="WmiMap"/> through MSI's WMI1, which
/// goes through Windows' own EC driver and so never races it; only Cooler Boost and the charge limit
/// (<see cref="WmiMap.PortRegisters"/>) through the raw port protocol, and only when a port is given.
/// Any other register is refused before anything is touched, so the port can never be used for
/// polling or for a register Windows might be using. Writes are also held to
/// <see cref="EcWriteRules.WritableRegisters"/>, a second line behind <see cref="EcGateway"/>, because
/// WMI itself stores any value in any field. Not thread-safe: use it only from <see cref="EcWorker"/>.
/// A block costs one <see cref="IWmiFields.Read"/> per class, but <see cref="RetryingEcReader"/>
/// splits blocks into single reads; that is fine, as the WMI backend reads one field per call anyway.
/// </summary>
public sealed class RoutedEcRegisters : IEcRegisters, IEcWritableRegisters
{
    private const int RegisterCount = 256;

    private readonly IWmiFields _wmi;
    private readonly IEcWritableRegisters? _port;

    /// <param name="port">Null runs WMI only: Cooler Boost and the charge limit are then refused too.</param>
    internal RoutedEcRegisters(IWmiFields wmi, IEcWritableRegisters? port)
    {
        _wmi = wmi;
        _port = port;
    }

    /// <param name="port">Null runs WMI only: Cooler Boost and the charge limit are then refused too.</param>
    public static RoutedEcRegisters Create(IWmiFields wmi, EcController? port) => new(wmi, port);

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
        foreach (var group in registers.Where(WmiMap.Fields.ContainsKey).GroupBy(r => WmiMap.Fields[r].ClassName))
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

        if (WmiMap.Fields.TryGetValue(register, out var field))
        {
            _wmi.Write(field.ClassName, field.Index, value);
            return;
        }

        _port!.Write(register, value);
    }

    private void ReadWmiClass(string className, IReadOnlyList<byte> registers, Dictionary<byte, byte> values)
    {
        var fields = registers.Select(r => WmiMap.Fields[r]).ToArray();
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

    /// <summary>Programming errors, not EC trouble: never retried, never sent anywhere.</summary>
    private void EnsureRoutable(byte register)
    {
        if (WmiMap.Fields.ContainsKey(register))
        {
            return;
        }

        if (!WmiMap.PortRegisters.Contains(register))
        {
            throw new InvalidOperationException($"0x{register:X2} WMI haritasında yok ve port için izinli değil.");
        }

        if (_port is null)
        {
            throw new InvalidOperationException($"0x{register:X2} yalnızca port üzerinden erişilebilir; port kapalı.");
        }
    }
}
