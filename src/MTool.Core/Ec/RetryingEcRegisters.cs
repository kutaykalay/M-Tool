namespace MTool.Core.Ec;

/// <summary>
/// Repeats a single read that failed with <see cref="EcAccessException"/>, waiting
/// <see cref="EcAccessRetry.Delays"/> in between. Other exceptions pass through. One instance
/// serves one operation, so <see cref="EcAccessRetry.SleepBudget"/> bounds the whole operation.
/// Used only on the EC worker thread.
/// </summary>
internal class RetryingEcReader(IEcRegisters inner, EcAccessRetry retry, Action<string> warn) : IEcRegisters
{
    private const int RegisterCount = 256;

    private TimeSpan _slept;

    public byte Read(byte register) =>
        Retry($"0x{register:X2} okuma", () => inner.Read(register));

    public IReadOnlyList<byte> ReadBlock(byte startRegister, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startRegister + count, RegisterCount, nameof(count));

        var values = new byte[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = Read((byte)(startRegister + i));
        }

        return Array.AsReadOnly(values);
    }

    protected T Retry<T>(string access, Func<T> operation)
    {
        for (var retryNumber = 0; ; retryNumber++)
        {
            try
            {
                return operation();
            }
            catch (EcAccessException ex) when (MayRetry(retryNumber))
            {
                var delay = retry.Delays[retryNumber];
                _slept += delay;
                warn($"EC cevap vermedi ({access}: {ex.Message}); {delay.TotalMilliseconds:F0} ms sonra yeniden " +
                     $"deneniyor ({retryNumber + 1}/{retry.Delays.Count}).");
                retry.Sleep(delay);
            }
        }
    }

    private bool MayRetry(int retryNumber) =>
        retryNumber < retry.Delays.Count && _slept + retry.Delays[retryNumber] <= retry.SleepBudget;
}

/// <summary>
/// <see cref="RetryingEcReader"/> for writes too. Writes are safe to repeat because the gateway
/// only writes absolute values and reads every one of them back. One instance serves one plan.
/// </summary>
internal sealed class RetryingEcRegisters(IEcWritableRegisters inner, EcAccessRetry retry, Action<string> warn)
    : RetryingEcReader(inner, retry, warn), IEcWritableRegisters
{
    private readonly IEcWritableRegisters _writable = inner;

    public void Write(byte register, byte value) =>
        Retry($"0x{register:X2}=0x{value:X2} yazma", () =>
        {
            _writable.Write(register, value);
            return true;
        });
}
