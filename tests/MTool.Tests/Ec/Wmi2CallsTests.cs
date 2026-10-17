using System.Reflection;
using MTool.Core.Diagnostics;
using MTool.Core.Ec;

namespace MTool.Tests.Ec;

/// <summary>
/// The WMI2 calls M-Tool may make (Aşama 10, CLAUDE.md "WMI2 canlı okuma"): a pinned list of
/// <c>Get_*</c> methods, <c>Get_Data</c> only at a pinned set of addresses, never a <c>Set_*</c>.
/// </summary>
public class Wmi2CallsTests
{
    private static readonly byte[] SensorRegisters = [0x68, 0x80, 0x71, 0x89, 0xC8, 0xC9, 0xCA, 0xCB];
    private static readonly byte[] StatusRegisters = [0x98, 0xD2, 0xD4, 0xD7];

    public static TheoryData<string, IReadOnlyList<Wmi2Request>> Lists() => new()
    {
        { nameof(Wmi2Calls.Report), Wmi2Calls.Report },
        { nameof(Wmi2Calls.Live), Wmi2Calls.Live },
        { nameof(Wmi2Calls.Status), Wmi2Calls.Status },
        { nameof(Wmi2Calls.Allowed), Wmi2Calls.Allowed },
    };

    [Theory]
    [MemberData(nameof(Lists))]
    public void Every_list_holds_read_methods_only(string name, IReadOnlyList<Wmi2Request> list)
    {
        list.Should().NotBeEmpty(name)
            .And.OnlyContain(r => r.Method.StartsWith("Get_", StringComparison.Ordinal), name)
            .And.NotContain(r => r.Method.StartsWith("Set_", StringComparison.OrdinalIgnoreCase), name);
    }

    [Fact]
    public void The_data_registers_are_exactly_the_pinned_sensor_and_status_addresses()
    {
        Wmi2Calls.SensorRegisters.Should().Equal(SensorRegisters);
        Wmi2Calls.StatusRegisters.Should().Equal(StatusRegisters);
        Wmi2Calls.DataRegisters.Should().Equal(SensorRegisters.Concat(StatusRegisters)).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_allowed_call_passes_the_check()
    {
        Wmi2Calls.Allowed.Should().OnlyContain(r => Wmi2Calls.IsAllowed(r.Method, r.Sub));
    }

    [Fact]
    public void The_live_list_reads_each_sensor_register_once_with_get_data()
    {
        Wmi2Calls.Live.Should().OnlyContain(r => r.Method == Wmi2Calls.GetData);
        Wmi2Calls.Live.Select(r => r.Sub).Should().Equal(SensorRegisters);
    }

    [Fact]
    public void The_status_list_reads_each_status_register_once_with_get_data()
    {
        Wmi2Calls.Status.Should().OnlyContain(r => r.Method == Wmi2Calls.GetData);
        Wmi2Calls.Status.Select(r => r.Sub).Should().Equal(StatusRegisters);
    }

    [Fact]
    public void Get_data_is_never_allowed_outside_the_data_registers()
    {
        Wmi2Calls.Allowed.Where(r => r.Method == Wmi2Calls.GetData).Select(r => r.Sub)
            .Should().OnlyContain(sub => Wmi2Calls.DataRegisters.Contains(sub));
    }

    [Fact]
    public void Every_list_is_part_of_the_allowed_list()
    {
        Wmi2Calls.Allowed.Should().Contain(Wmi2Calls.Report)
            .And.Contain(Wmi2Calls.Live)
            .And.Contain(Wmi2Calls.Status)
            .And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_report_list_is_the_packets_then_every_data_register_once()
    {
        // The Worker accepts Get_Data at these addresses (M-Tool-reports, "accept the pinned Get_Data
        // reads"); a report with any other call would be refused with 400.
        Wmi2Calls.Report.Select(r => (r.Method, r.Sub)).Should().Equal(
            new (string, byte)[]
            {
                ("Get_WMI", 0), ("Get_EC", 0),
                ("Get_Temperature", 0), ("Get_Temperature", 1), ("Get_Temperature", 2),
                ("Get_Fan", 0), ("Get_Fan", 1), ("Get_Fan", 2),
                ("Get_Thermal", 1), ("Get_Thermal", 2),
                ("Get_AP", 0), ("Get_AP", 1),
            }.Concat(SensorRegisters.Concat(StatusRegisters).Select(register => (Wmi2Calls.GetData, register))));
    }

    [Fact]
    public void The_report_reads_get_data_through_the_live_and_status_requests()
    {
        Wmi2Calls.Report.Where(r => r.Method == Wmi2Calls.GetData)
            .Should().Equal(Wmi2Calls.Live.Concat(Wmi2Calls.Status), ReferenceEquals);
    }

    [Fact]
    public void The_probe_runs_the_report_list()
    {
        Wmi2Probe.Calls.Should().Equal(Wmi2Calls.Report.Select(r => (r.Method, r.Sub)));
    }

    [Theory]
    [InlineData("Get_EC", 0, true)]
    [InlineData("Get_Data", 0x68, true)]
    [InlineData("Get_Data", 0xD7, true)]
    [InlineData("Get_Data", 0x00, false)]
    [InlineData("Get_Data", 0xEF, false)]
    [InlineData("Set_Data", 0x68, false)]
    [InlineData("Get_X", 0, false)]
    [InlineData("get_ec", 0, false)]
    public void Is_allowed_matches_the_allowed_list_exactly(string method, byte sub, bool expected)
    {
        Wmi2Calls.IsAllowed(method, sub).Should().Be(expected);
    }

    [Fact]
    public void A_listed_call_is_found_by_name_and_sub()
    {
        Wmi2Calls.Request("Get_Fan", 1).Should().BeSameAs(Wmi2Calls.Report[6]);
        Wmi2Calls.DataRequest(0xD7).Should().BeSameAs(Wmi2Calls.Status[3]);
        Wmi2Calls.Request("Get_Data", 0x68).Should().BeSameAs(Wmi2Calls.Live[0]);
    }

    [Theory]
    [InlineData("Set_Data", 0x98)]
    [InlineData("Get_Data", 0x00)]
    [InlineData("Get_X", 0)]
    public void An_unlisted_call_cannot_be_found(string method, byte sub)
    {
        var find = () => Wmi2Calls.Request(method, sub);

        find.Should().Throw<InvalidOperationException>().WithMessage($"*{method}(0x{sub:X2})*");
    }

    [Fact]
    public void A_request_cannot_be_made_outside_the_lists()
    {
        // No public or protected constructor, and no setter or init accessor that a "with" could use.
        typeof(Wmi2Request).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().NotContain(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly);
        typeof(Wmi2Request).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should().OnlyContain(p => p.SetMethod == null);
    }

    [Fact]
    public void Only_the_test_assembly_can_see_the_internal_constructor()
    {
        typeof(Wmi2Request).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName).Should().Equal("MTool.Tests");
    }

    [Fact]
    public void The_lists_cannot_be_changed_by_a_caller()
    {
        foreach (var list in new object[] { Wmi2Calls.Report, Wmi2Calls.Live, Wmi2Calls.Status, Wmi2Calls.Allowed, Wmi2Calls.DataRegisters })
        {
            list.Should().NotBeAssignableTo<Array>("a caller must not be able to cast a list back and change it");
            ((System.Collections.IList)list).IsReadOnly.Should().BeTrue("a caller must not be able to add a call");
        }
    }
}
