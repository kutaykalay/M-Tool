using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MTool.Core.Diagnostics;

namespace MTool.App.Hardware;

/// <summary>What a process scan found.</summary>
/// <param name="Companies">The file company of every distinct image (null entry = that program could not be read or was skipped).</param>
/// <param name="Truncated">The time budget ran out before every process was looked at.</param>
internal sealed record ProcessScanResult(IReadOnlyList<string?> Companies, bool Truncated);

/// <summary>
/// Reads the file company of the programs that are running, read-only. The image path comes from
/// <c>QueryFullProcessImageNameW</c> on a limited-rights handle (no module list, so no 32/64-bit trouble and
/// no hang on a protected process); only local drive-letter images are opened, so a network image can never stall the
/// scan; each image file is read once however many processes use it. The whole scan runs on a worker task and is
/// waited for at most the budget: a slow disk or a stuck read cuts the scan off instead of hanging the caller.
/// </summary>
internal static class ProcessImageScan
{
    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION.</summary>
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>The longest path Windows can return (32,767 characters plus the terminator).</summary>
    private const int MaxImagePath = 32768;

    /// <summary>Runs the scan for at most <paramref name="budget"/>; what was read by then is returned, marked truncated.</summary>
    /// <param name="listImages">The image path of every process (null = could not be queried).</param>
    /// <param name="readCompany">The file company of one image; may throw for reasons outside M-Tool.</param>
    /// <param name="budget">How long the caller waits.</param>
    public static ProcessScanResult Run(Func<IEnumerable<string?>> listImages, Func<string, string?> readCompany, TimeSpan budget)
    {
        var companies = new List<string?>();
        var gate = new object();
        var stopped = false;
        var listingFailed = false;

        void Append(string? company)
        {
            lock (gate)
            {
                if (!stopped)
                {
                    companies.Add(company);
                }
            }
        }

        var worker = Task.Run(() =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var image in listImages())
                {
                    if (Volatile.Read(ref stopped))
                    {
                        return;
                    }

                    if (image is null || !MofImagePathResolver.IsLocalDrivePath(image))
                    {
                        Append(null);
                    }
                    else if (seen.Add(image))
                    {
                        Append(ReadCompanyOf(image, readCompany));
                    }
                }
            }
            catch (Exception ex) when (!MsiSoftwareProbe.IsFatal(ex))
            {
                Volatile.Write(ref listingFailed, true);
            }
        });

        bool finished;
        try
        {
            finished = worker.Wait(budget);
        }
        catch (AggregateException ex)
        {
            ExceptionDispatchInfo.Capture(ex.GetBaseException()).Throw();
            throw;
        }

        lock (gate)
        {
            stopped = true;
            return new ProcessScanResult(companies.ToArray(), !finished || Volatile.Read(ref listingFailed));
        }
    }

    /// <summary>The scan as the probe runs it on this machine: running programs, company from the file's version info.</summary>
    public static ProcessScanResult RunOnThisMachine(TimeSpan budget) =>
        Run(ListRunningImages, path => FileVersionInfo.GetVersionInfo(path).CompanyName, budget);

    private static string? ReadCompanyOf(string image, Func<string, string?> readCompany)
    {
        try
        {
            return readCompany(image) ?? string.Empty;
        }
        catch (Exception ex) when (!MsiSoftwareProbe.IsFatal(ex))
        {
            return null;
        }
    }

    private static IEnumerable<string?> ListRunningImages()
    {
        var path = new StringBuilder(MaxImagePath);
        foreach (var process in Process.GetProcesses())
        {
            int id;
            using (process)
            {
                id = process.Id;
            }

            yield return QueryImagePath(id, path);
        }
    }

    private static string? QueryImagePath(int processId, StringBuilder buffer)
    {
        using var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var size = buffer.Capacity;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder exeName, ref int size);
}
