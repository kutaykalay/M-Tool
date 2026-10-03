namespace MTool.Core.Ec;

public sealed class EcAccessException : Exception
{
    public EcAccessException(string message)
        : base(message)
    {
    }

    public EcAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// True when the access gate refused the operation (sleep, or just after a wake) before any EC
    /// access: nothing was read or written, so it says nothing about the EC.
    /// </summary>
    public bool IsAccessPaused { get; init; }
}
