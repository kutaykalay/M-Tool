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
}
