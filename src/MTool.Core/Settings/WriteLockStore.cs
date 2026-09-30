namespace MTool.Core.Settings;

/// <summary>
/// Persists a failed-write lock (<c>write-lock.txt</c>) so every later session starts with EC writes
/// locked until the file is cleared on purpose.
/// </summary>
public sealed class WriteLockStore(string directory)
{
    private const string FileName = "write-lock.txt";

    private string FilePath => Path.Combine(directory, FileName);

    /// <summary>The recorded reason, or null when writes are not locked.</summary>
    public string? Reason => File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;

    public void Lock(string reason)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {reason}");
    }

    public void Clear() => File.Delete(FilePath);
}
