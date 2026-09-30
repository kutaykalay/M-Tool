namespace MTool.Core.Ec;

public enum EcOperation
{
    Read,
    Write,
}

/// <summary>Where an EC transaction attempt gave up.</summary>
public enum EcFailureKind
{
    /// <summary>A byte was already waiting in the output buffer before our command.</summary>
    OutputPendingAtStart,

    /// <summary>IBF stayed set: the EC did not accept a command, address or value in time.</summary>
    InputBufferBusy,

    /// <summary>The read address was accepted but OBF never rose.</summary>
    NoAnswer,

    /// <summary>A second byte was queued behind the answer, so the answer is ambiguous.</summary>
    ExtraByteAfterAnswer,
}

/// <param name="Status">The last status byte (port 0x66) seen by the failing step.</param>
/// <param name="Drained">Bytes the settle step took out of the output buffer after this attempt.</param>
public sealed record EcFailedAttempt(int Attempt, EcFailureKind Kind, byte Status, IReadOnlyList<byte> Drained);

/// <summary>One read or write that needed more than one attempt, or failed altogether.</summary>
public sealed record EcTransactionTrouble(
    EcOperation Operation,
    byte Register,
    bool Succeeded,
    IReadOnlyList<EcFailedAttempt> Attempts)
{
    public override string ToString() =>
        $"{Operation} 0x{Register:X2} {(Succeeded ? "kurtarıldı" : "BAŞARISIZ")}: " +
        string.Join(", ", Attempts.Select(a =>
            $"#{a.Attempt} {a.Kind} durum=0x{a.Status:X2}" +
            (a.Drained.Count == 0 ? string.Empty : $" boşaltılan=[{string.Join(' ', a.Drained.Select(b => b.ToString("X2")))}]")));
}
