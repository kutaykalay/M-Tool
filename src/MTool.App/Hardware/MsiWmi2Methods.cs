using System.Management;
using MTool.Core.Diagnostics;

namespace MTool.App.Hardware;

/// <summary>
/// Calls the read methods of MSI's WMI2 interface (<c>MSI_ACPI</c>, newer laptops) for a report.
/// Read-only by construction: a method whose name does not start with "Get_" is refused before
/// any WMI access. The instance name and the packet format come from YAMDCC ([assumption], never
/// tested on hardware here).
/// </summary>
internal static class MsiWmi2Methods
{
    private const string InstancePath = @"MSI_ACPI.InstanceName='ACPI\PNP0C14\0_0'";
    private const string ReadPrefix = "Get_";
    private const string DataParameter = "Data";
    private const string BytesProperty = "Bytes";
    private const string EmbeddedTypePrefix = "object:";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Opens the instance once, then runs every <see cref="Wmi2Probe.Calls"/> through it.</summary>
    /// <exception cref="Core.Ec.EcAccessException">The instance cannot be opened.</exception>
    public static Wmi2Readout Probe()
    {
        using var instance = MsiWmiFields.Bounded("WMI2 bağlantısı", CallTimeout, Open);
        return Wmi2Probe.Run((method, sub) => Call(instance, method, sub));
    }

    /// <summary>A 32-byte packet whose first byte is <paramref name="sub"/>; the answer's bytes as they came.</summary>
    internal static byte[] Call(ManagementObject instance, string method, byte sub)
    {
        if (!method.StartsWith(ReadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Yalnızca okuma yöntemleri çağrılabilir: {method}");
        }

        return MsiWmiFields.Bounded($"WMI2 {method}({sub})", CallTimeout, () =>
        {
            using var parameters = instance.GetMethodParameters(method);
            using var data = (parameters[DataParameter] as ManagementBaseObject) ?? NewData(instance, parameters);
            var packet = new byte[Wmi2Probe.PacketLength];
            packet[0] = sub;
            data[BytesProperty] = packet;
            parameters[DataParameter] = data;
            using var result = instance.InvokeMethod(method, parameters, null)
                ?? throw new InvalidOperationException($"{method} sonuç döndürmedi.");
            using var answer = result[DataParameter] as ManagementBaseObject
                ?? throw new InvalidOperationException($"{method} cevabında {DataParameter} yok.");
            return answer[BytesProperty] as byte[]
                ?? throw new InvalidOperationException($"{method} cevabında {BytesProperty} bayt dizisi değil.");
        });
    }

    private static ManagementObject Open()
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
            throw new InvalidOperationException($"WMI2 giriş parametresinin türü bilinmiyor: {type ?? "yok"}");
        }

        using var dataClass = new ManagementClass(instance.Scope, new ManagementPath(type[EmbeddedTypePrefix.Length..]), null);
        return dataClass.CreateInstance();
    }
}
