namespace MTool.Core.Localization;

/// <summary>
/// An exception's <see cref="Exception.Message"/> is what the user reads, in the UI language. A message built
/// from a translated text also carries its English twin for the log, so a log reads the same whatever language
/// the user runs.
/// </summary>
public static class ExceptionLogText
{
    private const string LogKey = "MTool.LogMessage";

    /// <summary>Attaches the English twin of the message; returns the same exception, to be thrown.</summary>
    public static TException WithLog<TException>(this TException exception, string log)
        where TException : Exception
    {
        exception.Data[LogKey] = log;
        return exception;
    }

    /// <summary>The English twin if there is one, else the message (a system or English message).</summary>
    public static string ForLog(this Exception exception) => exception.Data[LogKey] as string ?? exception.Message;

    /// <summary>The whole exception (type, message, stack, inner exceptions) with its own message in English.</summary>
    public static string ToLogString(this Exception exception) =>
        exception.Data[LogKey] is string log
            ? exception.ToString().Replace($"{exception.GetType()}: {exception.Message}", $"{exception.GetType()}: {log}", StringComparison.Ordinal)
            : exception.ToString();

    internal static UserAndLogText ForUserAndLog(this Exception exception) => new(exception.Message, exception.ForLog());
}
