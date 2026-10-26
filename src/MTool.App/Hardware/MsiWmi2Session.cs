using System.Management;
using MTool.App.Resources;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;
using MTool.Core.Localization;

namespace MTool.App.Hardware;

/// <summary>
/// One open <c>MSI_ACPI</c> instance (MSI's WMI2 interface, newer laptops) and the only way M-Tool
/// calls its methods. Every read is checked against <see cref="Wmi2Calls.Allowed"/> and the "Get_"
/// prefix before any WMI access. Only a session from <see cref="OpenWritable"/> can call
/// <c>Set_Data</c>, and only with a request the write ceiling allows. The instance name and the packet
/// format come from YAMDCC and GhostDeck ([assumption], never tested on hardware here).
/// </summary>
/// <remarks>
/// A call that does not answer in time is abandoned on its thread but may still be using the
/// instance, which is not thread-safe. While it runs the session refuses every call, read or write,
/// and the instance is disposed only once no call is running on it. An abandoned <c>Set_Data</c> may
/// still land late: the gateway has already locked WMI2 writes for the session by then.
/// </remarks>
internal sealed class MsiWmi2Session : IWmi2Methods, IWmi2DataWriter, IDisposable
{
    private const string InstancePath = @"MSI_ACPI.InstanceName='ACPI\PNP0C14\0_0'";
    private const string ReadPrefix = "Get_";
    private const string DataParameter = "Data";
    private const string BytesProperty = "Bytes";
    private const string EmbeddedTypePrefix = "object:";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    private readonly IDisposable _instance;
    private readonly Func<Wmi2Request, byte[]> _invoke;
    private readonly Func<byte[], byte[]>? _setData;
    private readonly TimeSpan _timeout;
    private int _running;
    private int _disposed;
    private int _instanceDisposed;

    /// <param name="invoke">One raw call on <paramref name="instance"/>, without a time limit.</param>
    /// <param name="setData">One raw <c>Set_Data</c> call with the given 32-byte packet, without a time limit; null means read-only.</param>
    internal MsiWmi2Session(
        IDisposable instance, Func<Wmi2Request, byte[]> invoke, TimeSpan timeout, Func<byte[], byte[]>? setData = null) =>
        (_instance, _invoke, _timeout, _setData) = (instance, invoke, timeout, setData);

    /// <summary>
    /// Opens the instance. If opening times out, an instance that opens later is not disposed: one
    /// object for the life of the process, and only after a user action.
    /// </summary>
    /// <exception cref="EcAccessException">The instance cannot be opened.</exception>
    public static MsiWmi2Session Open() => Open(writable: false);

    /// <summary>
    /// Like <see cref="Open"/>, but the session can also call <c>Set_Data</c>. Only for experimental control
    /// (CLAUDE.md, "WMI2 deneysel yazma"); the method name is fixed.
    /// </summary>
    /// <exception cref="EcAccessException">The instance cannot be opened.</exception>
    public static MsiWmi2Session OpenWritable() => Open(writable: true);

    private static MsiWmi2Session Open(bool writable)
    {
        var instance = MsiWmiFields.Bounded("MSI_ACPI connection", CallTimeout, OpenInstance);
        return new MsiWmi2Session(
            instance,
            request => InvokeRead(instance, request),
            CallTimeout,
            writable ? packet => Invoke(instance, Wmi2Writes.SetData, packet) : null);
    }

    /// <summary>
    /// Opens the instance once, then runs every <see cref="Wmi2Calls.Report"/> call through it, each
    /// under the EC lock so it never runs alongside a live round in the reports window.
    /// </summary>
    /// <exception cref="EcAccessException">The instance cannot be opened.</exception>
    public static Wmi2Readout Probe()
    {
        using var ecLock = new AccessEcMutex();
        using var session = Open();
        return Probe(new LockedWmi2Methods(session, ecLock, EcSession.LockTimeout));
    }

    /// <summary>The report's calls, each looked up in <see cref="Wmi2Calls"/>: a name not on the list cannot be called.</summary>
    internal static Wmi2Readout Probe(IWmi2Methods methods) =>
        Wmi2Probe.Run((method, sub) => methods.Call(Wmi2Calls.Request(method, sub)));

    /// <exception cref="InvalidOperationException">The call is not on the list; WMI was not touched.</exception>
    /// <exception cref="EcAccessException">The call failed or timed out, or an earlier one never answered.</exception>
    public byte[] Call(Wmi2Request request)
    {
        EnsureAllowed(request);
        return RunBounded($"MSI_ACPI {request.Method}(0x{request.Sub:X2})", () => _invoke(request));
    }

    /// <summary>One <c>Set_Data</c> call: only on a session from <see cref="OpenWritable"/>, only for a request inside the ceiling.</summary>
    /// <exception cref="InvalidOperationException">The session is read-only, or the request is outside the ceiling; WMI was not touched.</exception>
    /// <exception cref="EcAccessException">The call failed or timed out, or an earlier one never answered.</exception>
    public byte[] SetData(Wmi2WriteRequest request)
    {
        var setData = _setData ?? throw new InvalidOperationException("This WMI2 session is read-only.");
        var (register, value) = (request.Register, request.Value);
        if (!Wmi2Writes.IsWithinCeiling(register, value))
        {
            throw new InvalidOperationException(
                $"{Wmi2Writes.SetData}(0x{register:X2}, 0x{value:X2}) is outside the WMI2 write ceiling.");
        }

        var packet = new byte[Wmi2Probe.PacketLength];
        (packet[0], packet[1]) = (register, value);
        return RunBounded($"MSI_ACPI {Wmi2Writes.SetData}(0x{register:X2}, 0x{value:X2})", () => setData(packet));
    }

    /// <summary>The one path of every call, read or write: disposed check, the single running gate, the time limit.</summary>
    private byte[] RunBounded(string name, Func<byte[]> work)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            var (text, log) = AppText.Say(nameof(Strings.Wmi2_StillRunning));
            throw new EcAccessException(text).WithLog(log);
        }

        return MsiWmiFields.Bounded(name, _timeout, () =>
        {
            try
            {
                return work();
            }
            finally
            {
                Finished();
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && Volatile.Read(ref _running) == 0)
        {
            DisposeInstance();
        }
    }

    /// <exception cref="InvalidOperationException">Not a read method, or not on M-Tool's list.</exception>
    internal static void EnsureAllowed(Wmi2Request request)
    {
        var (method, sub) = (request.Method, request.Sub);
        if (!method.StartsWith(ReadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Only read methods can be called: {method}");
        }

        if (!Wmi2Calls.IsAllowed(method, sub))
        {
            throw new InvalidOperationException($"{method}(0x{sub:X2}) is not on M-Tool's WMI2 list.");
        }
    }

    /// <summary>A call that was abandoned may finish after Dispose: the last one to finish disposes the instance.</summary>
    private void Finished()
    {
        if (Interlocked.Decrement(ref _running) == 0 && Volatile.Read(ref _disposed) == 1)
        {
            DisposeInstance();
        }
    }

    private void DisposeInstance()
    {
        if (Interlocked.Exchange(ref _instanceDisposed, 1) == 0)
        {
            _instance.Dispose();
        }
    }

    /// <summary>A 32-byte packet whose first byte is the request's sub.</summary>
    private static byte[] InvokeRead(ManagementObject instance, Wmi2Request request)
    {
        var packet = new byte[Wmi2Probe.PacketLength];
        packet[0] = request.Sub;
        return Invoke(instance, request.Method, packet);
    }

    /// <summary>One raw call with the given packet; the answer's bytes as they came.</summary>
    private static byte[] Invoke(ManagementObject instance, string method, byte[] packet)
    {
        using var parameters = instance.GetMethodParameters(method);
        using var data = (parameters[DataParameter] as ManagementBaseObject) ?? NewData(instance, parameters);
        data[BytesProperty] = packet;
        parameters[DataParameter] = data;
        using var result = instance.InvokeMethod(method, parameters, null)
            ?? throw new InvalidOperationException($"{method} returned no result.");
        using var answer = result[DataParameter] as ManagementBaseObject
            ?? throw new InvalidOperationException($"The answer of {method} has no {DataParameter}.");
        return answer[BytesProperty] as byte[]
            ?? throw new InvalidOperationException($"The {BytesProperty} of the answer of {method} is not a byte array.");
    }

    private static ManagementObject OpenInstance()
    {
        var opened = new ManagementObject(MsiWmiFields.Connect(), new ManagementPath(InstancePath), null);
        try
        {
            opened.Get();
            return opened;
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    /// <summary>
    /// An in-parameter of an embedded type is usually empty until one is assigned: its class is in
    /// the parameter's CIMTYPE qualifier ("object:ClassName").
    /// </summary>
    private static ManagementBaseObject NewData(ManagementObject instance, ManagementBaseObject parameters)
    {
        var type = parameters.Properties[DataParameter].Qualifiers["CIMTYPE"].Value as string;
        if (type is null || !type.StartsWith(EmbeddedTypePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The type of the WMI2 input parameter is unknown: {type ?? "none"}");
        }

        using var dataClass = new ManagementClass(instance.Scope, new ManagementPath(type[EmbeddedTypePrefix.Length..]), null);
        return dataClass.CreateInstance();
    }
}
