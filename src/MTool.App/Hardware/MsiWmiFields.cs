using System.Collections.Frozen;
using System.Management;
using System.Text.RegularExpressions;
using MTool.Core.Device;
using MTool.Core.Ec;

namespace MTool.App.Hardware;

/// <summary>
/// <see cref="IWmiFields"/> over MSI's WMI1 classes in <c>root\WMI</c>, through System.Management.
/// Each field is one instance (<c>InstanceName</c> = prefix + index) with one byte-sized value
/// property named after the class (<c>MSI_CPU.CPU</c>). Only the (class, index) pairs in the
/// session's <see cref="WmiFieldMap"/> are read, and only those of the verified P65 map's writable
/// registers (<see cref="WmiMap"/>) are written, so no caller can build another WMI path.
/// <para>
/// Every WMI call runs on a thread-pool (MTA) thread with a hard <see cref="CallTimeout"/>: the
/// options' own timeouts do not bound synchronous calls, and a hung call would hold the Access_EC
/// lock forever. Every failure becomes an <see cref="EcAccessException"/> (retried, and every
/// write is read back), never a hard failure that would lock writes; after one, the next call
/// reconnects in case the WMI service restarted. Used only from the EC worker thread.
/// </para>
/// </summary>
internal sealed class MsiWmiFields : IWmiFields
{
    private const string Namespace = @"\\.\root\WMI";
    private const string ClassPrefix = "MSI_";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    // A prefix that cannot break out of the quoted InstanceName key, e.g. ACPI\PNP0C14\0_
    private static readonly Regex SafePrefix = new(@"^[A-Za-z0-9\\]+_$", RegexOptions.CultureInvariant);

    private static readonly FrozenSet<WmiField> Writable = EcWriteRules.WritableRegisters
        .Where(WmiMap.Fields.ContainsKey)
        .Select(r => WmiMap.Fields[r])
        .ToFrozenSet();

    private readonly string _instancePrefix;
    private readonly WmiFieldMap _map;
    private ManagementScope _scope;
    private bool _reconnect;

    private MsiWmiFields(ManagementScope scope, string instancePrefix, WmiFieldMap map) =>
        (_scope, _instancePrefix, _map) = (scope, instancePrefix, map);

    /// <summary>Connects and finds the instance prefix from the firmware class.</summary>
    /// <param name="map">The only fields this session may read.</param>
    /// <exception cref="EcAccessException">WMI1 is missing or does not answer.</exception>
    public static MsiWmiFields Open(WmiFieldMap map) => Bounded("bağlantı", CallTimeout, () =>
    {
        var scope = Connect();
        using var searcher = new ManagementObjectSearcher(
            scope, new SelectQuery(WmiMap.SoftwareClass), new EnumerationOptions { Timeout = CallTimeout });
        using var instances = searcher.Get();
        var names = instances.Cast<ManagementObject>().Select(InstanceNameOf).ToArray();
        return new MsiWmiFields(scope, PrefixOf(names), map);
    });

    public IReadOnlyList<int> Read(string className, IReadOnlyList<int> indices)
    {
        foreach (var index in indices)
        {
            EnsureMapped(_map, className, index);
        }

        return Call($"{className} okuma", scope => indices.Select(i => ReadOne(scope, className, i)).ToArray());
    }

    /// <summary>
    /// The static <see cref="Writable"/> set (verified P65 map, writable registers) is the authority;
    /// the session map only narrows what may be touched at all.
    /// </summary>
    public void Write(string className, int index, byte value)
    {
        EnsureMapped(_map, className, index);
        if (!Writable.Contains(new WmiField(className, index)))
        {
            throw new InvalidOperationException($"{className}[{index}] yazılabilir bir register'a ait değil.");
        }

        Call($"{className}[{index}] yazma", scope =>
        {
            using var field = Get(scope, className, index);
            field[ValueProperty(className)] = value;
            field.Put(new PutOptions { Type = PutType.UpdateOnly });
            return true;
        });
    }

    /// <summary>Programming errors, not WMI trouble: never retried.</summary>
    internal static void EnsureMapped(WmiFieldMap map, string className, int index)
    {
        if (!map.FieldSet.Contains(new WmiField(className, index)))
        {
            throw new InvalidOperationException($"{className}[{index}] WMI haritasında yok.");
        }
    }

    /// <summary>WMI object paths need double quotes and doubled backslashes; single quotes give "Not found".</summary>
    internal static string PathOf(string className, string instancePrefix, int index) =>
        $"{className}.InstanceName=\"{(instancePrefix + index).Replace(@"\", @"\\")}\"";

    internal static string ValueProperty(string className) => className[ClassPrefix.Length..];

    /// <summary>All instances share one safe prefix ending in <c>_</c>, followed by the index.</summary>
    internal static string PrefixOf(IReadOnlyList<string> instanceNames)
    {
        var prefixes = instanceNames
            .Select(n => n.LastIndexOf('_') is var end and >= 0 ? n[..(end + 1)] : null)
            .Distinct()
            .ToArray();
        return prefixes is [{ } prefix] && SafePrefix.IsMatch(prefix)
            ? prefix
            : throw new EcAccessException($"{WmiMap.SoftwareClass} örnek adları beklenmeyen biçimde: {string.Join(", ", instanceNames)}");
    }

    /// <summary>
    /// Runs <paramref name="access"/> on a thread-pool (MTA) thread and gives up after
    /// <paramref name="timeout"/>. Anything it throws, and the timeout, becomes an <see cref="EcAccessException"/>.
    /// A hung call is abandoned on its thread; its late failure is observed and dropped.
    /// </summary>
    internal static T Bounded<T>(string what, TimeSpan timeout, Func<T> access)
    {
        var call = Task.Run(access);
        bool finished;
        try
        {
            finished = call.Wait(timeout);
        }
        catch (AggregateException ex) when (ex.InnerException is EcAccessException inner)
        {
            throw inner;
        }
        catch (AggregateException ex) when (ex.InnerException is { } inner)
        {
            throw new EcAccessException($"WMI {what}: {inner.Message}", inner);
        }

        if (!finished)
        {
            _ = call.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new EcAccessException($"WMI {what}: {timeout.TotalSeconds:0.#} sn içinde cevap gelmedi (zaman aşımı).");
        }

        return call.Result;
    }

    private T Call<T>(string what, Func<ManagementScope, T> access)
    {
        try
        {
            return Bounded(what, CallTimeout, () =>
            {
                if (_reconnect)
                {
                    _scope = Connect();
                    _reconnect = false;
                }

                return access(_scope);
            });
        }
        catch (EcAccessException)
        {
            // The WMI service may have restarted under us: a stale scope would fail forever.
            _reconnect = true;
            throw;
        }
    }

    internal static ManagementScope Connect()
    {
        var scope = new ManagementScope(Namespace, new ConnectionOptions { Timeout = CallTimeout });
        scope.Connect();
        return scope;
    }

    private int ReadOne(ManagementScope scope, string className, int index)
    {
        using var field = Get(scope, className, index);
        return Convert.ToInt32(field[ValueProperty(className)]
            ?? throw new EcAccessException($"{className}[{index}] değer döndürmedi."));
    }

    private ManagementObject Get(ManagementScope scope, string className, int index)
    {
        var field = new ManagementObject(scope, new ManagementPath(PathOf(className, _instancePrefix, index)), new ObjectGetOptions());
        try
        {
            field.Get();
            return field;
        }
        catch
        {
            field.Dispose();
            throw;
        }
    }

    private static string InstanceNameOf(ManagementObject instance)
    {
        using (instance)
        {
            return instance["InstanceName"] as string
                ?? throw new EcAccessException($"{WmiMap.SoftwareClass} örneğinin InstanceName'i yok.");
        }
    }
}
