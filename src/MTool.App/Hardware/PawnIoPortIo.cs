using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MTool.App.Resources;
using MTool.Core.Ec;
using MTool.Core.Localization;

namespace MTool.App.Hardware;

/// <summary>
/// Port I/O through the PawnIO driver and its signed <c>LpcACPIEC</c> module (ports 0x62/0x66 only).
/// Talks to the driver solely via DeviceIoControl, as PawnIO's licence exception requires.
/// Not thread-safe; only <see cref="EcWorker"/>'s thread uses it.
/// </summary>
internal sealed class PawnIoPortIo : IPortIo, IDisposable
{
    private const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    private const string ModuleResource = "MTool.LpcACPIEC.bin";
    private const uint IoctlLoadBinary = 0xA1B22084; // CTL_CODE(41394, 0x821, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IoctlExecute = 0xA1B22104;    // CTL_CODE(41394, 0x841, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const int FunctionNameLength = 32;
    private const uint GenericReadWrite = 0xC0000000;
    private const uint ShareReadWrite = 0x3;
    private const uint OpenExisting = 3;

    private readonly SafeFileHandle _handle;

    private PawnIoPortIo(SafeFileHandle handle) => _handle = handle;

    public static PawnIoPortIo Open()
    {
        var handle = CreateFile(DevicePath, GenericReadWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw Failure(nameof(Strings.PawnIo_OpenFailed), new Win32Exception(Marshal.GetLastWin32Error()));
        }

        try
        {
            var module = LoadModuleBytes();
            if (!DeviceIoControl(handle, IoctlLoadBinary, module, module.Length, null, 0, out _, IntPtr.Zero))
            {
                throw Failure(nameof(Strings.PawnIo_ModuleFailed), new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return new PawnIoPortIo(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public byte In(byte port)
    {
        var value = Execute("ioctl_pio_read", [port], 1)[0];
        return value is >= byte.MinValue and <= byte.MaxValue
            ? (byte)value
            : throw Failure(nameof(Strings.PawnIo_BadReply), inner: null, "ioctl_pio_read", $"port value {value}");
    }

    public void Out(byte port, byte value) => Execute("ioctl_pio_write", [port, value], 0);

    public void Dispose() => _handle.Dispose();

    /// <summary>In the user's language, with the English twin for the log.</summary>
    private static EcAccessException Failure(string key, Exception? inner, params object?[] args)
    {
        var (text, log) = AppText.Say(key, args);
        var failure = inner is null ? new EcAccessException(text) : new EcAccessException(text, inner);
        return failure.WithLog(log);
    }

    private long[] Execute(string function, long[] input, int outputCount)
    {
        var request = new byte[FunctionNameLength + (input.Length * sizeof(long))];
        Encoding.ASCII.GetBytes(function).CopyTo(request, 0);
        Buffer.BlockCopy(input, 0, request, FunctionNameLength, input.Length * sizeof(long));
        var response = new byte[outputCount * sizeof(long)];

        if (!DeviceIoControl(_handle, IoctlExecute, request, request.Length, response, response.Length, out var written, IntPtr.Zero))
        {
            throw Failure(nameof(Strings.PawnIo_CallFailed), new Win32Exception(Marshal.GetLastWin32Error()), function);
        }

        // A short reply must not turn into a valid-looking zero ("IBF clear", register value 0).
        if (written != response.Length)
        {
            throw Failure(nameof(Strings.PawnIo_BadReply), inner: null, function, $"{written} of {response.Length} bytes");
        }

        var result = new long[outputCount];
        Buffer.BlockCopy(response, 0, result, 0, response.Length);
        return result;
    }

    private static byte[] LoadModuleBytes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ModuleResource)
            ?? throw new InvalidOperationException($"Embedded resource {ModuleResource} is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, byte[]? inBuffer, int inBufferSize,
        byte[]? outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
