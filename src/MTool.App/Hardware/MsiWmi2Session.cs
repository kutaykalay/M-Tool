using System.Management;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>
/// One open <c>MSI_ACPI</c> instance (MSI's WMI2 interface, newer laptops) and the only way M-Tool
/// calls its methods. Read-only by construction: every call is checked against
/// <see cref="Wmi2Calls.Allowed"/> and the "Get_" prefix before any WMI access. The instance name and
/// the packet format come from YAMDCC and GhostDeck ([assumption], never tested on hardware here).
/// </summary>
/// <remarks>
/// A call that does not answer in time is abandoned on its thread but may still be using the
/// instance, which is not thread-safe. From then on the session refuses every call, and the
/// instance is disposed only once no call is running on it.
/// </remarks>
internal sealed class MsiWmi2Session : IWmi2Methods, IDisposable
{
    private const string InstancePath = @"MSI_ACPI.InstanceName='ACPI\PNP0C14\0_0'";
    private const string ReadPrefix = "Get_";
    private const string DataParameter = "Data";
    private const string BytesProperty = "Bytes";
    private const string EmbeddedTypePrefix = "object:";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    private readonly IDisposable _instance;
    private readonly Func<Wmi2Request, byte[]> _invoke;
    private readonly TimeSpan _timeout;
    private int _running;
    private int _disposed;
    private int _instanceDisposed;

    /// <param name="invoke">One raw call on <paramref name="instance"/>, without a time limit.</param>
    internal MsiWmi2Session(IDisposable instance, Func<Wmi2Request, byte[]> invoke, TimeSpan timeout) =>
        (_instance, _invoke, _timeout) = (instance, invoke, timeout);

    /// <summary>
    /// Opens the instance. If opening times out, an instance that opens later is not disposed: one
    /// object for the life of the process, and only after a user action.
    /// </summary>
    /// <exception cref="EcAccessException">The instance cannot be opened.</exception>
    public static MsiWmi2Session Open()
    {
        var instance = MsiWmiFields.Bounded("MSI_ACPI connection", CallTimeout, OpenInstance);
        return new MsiWmi2Session(instance, request => Invoke(instance, request), CallTimeout);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new EcAccessException("MSI_ACPI: an earlier call is still running (it never answered); no call made.");
        }

        return MsiWmiFields.Bounded($"MSI_ACPI {request.Method}(0x{request.Sub:X2})", _timeout, () =>
        {
            try
            {
                return _invoke(request);
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

    /// <summary>A 32-byte packet whose first byte is the request's sub; the answer's bytes as they came.</summary>
    private static byte[] Invoke(ManagementObject instance, Wmi2Request request)
    {
        var method = request.Method;
        using var parameters = instance.GetMethodParameters(method);
        using var data = (parameters[DataParameter] as ManagementBaseObject) ?? NewData(instance, parameters);
        var packet = new byte[Wmi2Probe.PacketLength];
        packet[0] = request.Sub;
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
