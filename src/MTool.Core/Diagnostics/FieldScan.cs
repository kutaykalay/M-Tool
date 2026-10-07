namespace MTool.Core.Diagnostics;

/// <summary>
/// The mapped WMI1 fields, read one by one. On a model the map was not made for, fields may fail
/// or time out; after a few failures in a row the rest are skipped, so what was read is kept
/// instead of the whole scan running into the command timeout.
/// </summary>
/// <param name="Skipped">Fields not tried after the scan stopped.</param>
public sealed record FieldScan(IReadOnlyList<FieldReadout> Readouts, int Skipped)
{
    public const int MaxFailuresInARow = 3;

    /// <param name="tryRead">Null when the field could not be read; must not throw.</param>
    public static FieldScan Run(IReadOnlyList<(byte Register, string Field)> fields, Func<byte, byte?> tryRead)
    {
        var readouts = new List<FieldReadout>();
        var failuresInARow = 0;
        foreach (var (register, field) in fields)
        {
            if (failuresInARow == MaxFailuresInARow)
            {
                break;
            }

            var value = tryRead(register);
            failuresInARow = value is null ? failuresInARow + 1 : 0;
            readouts.Add(new FieldReadout(register, field, value));
        }

        return new FieldScan(readouts.AsReadOnly(), fields.Count - readouts.Count);
    }
}
