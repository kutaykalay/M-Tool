using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.Tests.Fakes;

/// <summary>
/// MSI WMI1 over a <see cref="FakeEcRegisters"/> memory: each field reads and writes the EC register
/// that <see cref="WmiMap"/> assigns to it, like the BIOS methods do.
/// </summary>
internal sealed class FakeWmiFields(FakeEcRegisters memory) : IWmiFields
{
    private static readonly IReadOnlyDictionary<WmiField, byte> Registers =
        WmiMap.Fields.ToDictionary(f => f.Value, f => f.Key);

    /// <summary>Every call in order, e.g. "read MSI_CPU[5,11]" or "write MSI_System[7]=0xC1".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The next this many calls fail with <see cref="EcAccessException"/> (WMI timeout).</summary>
    public int SilentCalls { get; set; }

    /// <summary>Overrides what a field reads (e.g. a value WMI should never return); null = memory.</summary>
    public Func<WmiField, int?>? ReadHook { get; set; }

    /// <summary>Writes to these fields are accepted but never reach the EC.</summary>
    public HashSet<WmiField> StuckFields { get; } = [];

    public IReadOnlyList<int> Read(string className, IReadOnlyList<int> indices)
    {
        Calls.Add($"read {className}[{string.Join(',', indices)}]");
        ThrowIfSilent();
        return indices.Select(i => new WmiField(className, i))
            .Select(f => ReadHook?.Invoke(f) ?? memory[RegisterOf(f)])
            .ToArray();
    }

    public void Write(string className, int index, byte value)
    {
        Calls.Add($"write {className}[{index}]=0x{value:X2}");
        ThrowIfSilent();
        var field = new WmiField(className, index);
        var register = RegisterOf(field);
        if (!StuckFields.Contains(field))
        {
            memory[register] = value;
        }
    }

    private static byte RegisterOf(WmiField field) =>
        Registers.TryGetValue(field, out var register)
            ? register
            : throw new InvalidOperationException($"{field} is not mapped in this fake.");

    private void ThrowIfSilent()
    {
        if (SilentCalls > 0)
        {
            SilentCalls--;
            throw new EcAccessException("WMI did not answer (simulated timeout).");
        }
    }
}
