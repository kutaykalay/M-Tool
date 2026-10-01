using MTool.App.Hardware;
using MTool.Core.Device;

namespace MTool.Tests.Hardware;

/// <summary>The pure parts of the WMI backend; the WMI calls themselves are tested by hand on the laptop.</summary>
public class MsiWmiFieldsTests
{
    [Fact]
    public void Paths_use_double_quotes_and_doubled_backslashes()
    {
        MsiWmiFields.PathOf(WmiMap.CpuClass, @"ACPI\PNP0C14\0_", 5)
            .Should().Be(@"MSI_CPU.InstanceName=""ACPI\\PNP0C14\\0_5""");
    }

    [Theory]
    [InlineData(WmiMap.CpuClass, "CPU")]
    [InlineData(WmiMap.SystemClass, "System")]
    [InlineData(WmiMap.SoftwareClass, "Software")]
    public void The_value_property_is_the_class_name_without_the_prefix(string className, string expected)
    {
        MsiWmiFields.ValueProperty(className).Should().Be(expected);
    }

    [Theory]
    [InlineData(WmiMap.CpuClass, 5)]
    [InlineData(WmiMap.SystemClass, 7)]
    [InlineData(WmiMap.ApClass, 2)]
    public void Mapped_fields_are_allowed(string className, int index)
    {
        ((Action)(() => MsiWmiFields.EnsureMapped(className, index))).Should().NotThrow();
    }

    [Theory]
    [InlineData(WmiMap.CpuClass, 0)] // exists in WMI, not in the map
    [InlineData(WmiMap.CpuClass, 99)]
    [InlineData(WmiMap.SystemClass, 8)]
    [InlineData("Win32_Process", 1)]
    [InlineData("MSI_CPU.InstanceName=\"x\" or 1", 1)]
    public void Anything_outside_the_map_is_refused(string className, int index)
    {
        ((Action)(() => MsiWmiFields.EnsureMapped(className, index))).Should().Throw<InvalidOperationException>();
    }

    // --- every WMI failure is an EC access error (retried, read back), never a hard failure ---

    public static TheoryData<Exception> WmiFailures => new()
    {
        new System.Runtime.InteropServices.COMException("RPC server unavailable", unchecked((int)0x800706BA)),
        new InvalidOperationException("thrown by System.Management itself"),
        new ArgumentException("bad path"),
        new ObjectDisposedException("scope"),
        new UnauthorizedAccessException(),
        new InvalidCastException(),
    };

    [Theory]
    [MemberData(nameof(WmiFailures))]
    public void Any_failure_inside_a_wmi_call_becomes_an_ec_access_error(Exception failure)
    {
        var act = () => MsiWmiFields.Bounded<int>("test", TimeSpan.FromSeconds(1), () => throw failure);

        act.Should().Throw<MTool.Core.Ec.EcAccessException>().WithInnerException(failure.GetType());
    }

    [Fact]
    public void An_ec_access_error_passes_through_unchanged()
    {
        var original = new MTool.Core.Ec.EcAccessException("already wrapped");

        var act = () => MsiWmiFields.Bounded<int>("test", TimeSpan.FromSeconds(1), () => throw original);

        act.Should().Throw<MTool.Core.Ec.EcAccessException>().Which.Should().BeSameAs(original);
    }

    [Fact]
    public void A_wmi_call_that_hangs_is_given_up_after_the_timeout()
    {
        using var never = new ManualResetEventSlim();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var act = () => MsiWmiFields.Bounded("test", TimeSpan.FromMilliseconds(100), () => never.Wait(TimeSpan.FromSeconds(5)));

        act.Should().Throw<MTool.Core.Ec.EcAccessException>().WithMessage("*zaman aşımı*");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        never.Set();
    }

    [Fact]
    public void A_wmi_call_runs_on_a_multithreaded_apartment_thread()
    {
        var apartment = MsiWmiFields.Bounded("test", TimeSpan.FromSeconds(1), Thread.CurrentThread.GetApartmentState);

        apartment.Should().Be(ApartmentState.MTA);
    }

    [Theory]
    [InlineData(@"ACPI\PNP0C14\0_6", @"ACPI\PNP0C14\0_")]
    [InlineData(@"ACPI\PNP0C14\1_25", @"ACPI\PNP0C14\1_")]
    public void The_instance_prefix_is_everything_up_to_the_last_underscore(string instanceName, string expected)
    {
        MsiWmiFields.PrefixOf([instanceName]).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"ACPI\PNP0C14\0_6", @"ACPI\PNP0C14\1_7")] // two different prefixes
    [InlineData("no-underscore")]
    [InlineData("ACPI\\x\" or 1=1 \"_6")] // a quote would break out of the InstanceName key
    public void An_unexpected_instance_layout_is_an_access_error(params string[] instanceNames)
    {
        ((Action)(() => MsiWmiFields.PrefixOf(instanceNames))).Should().Throw<MTool.Core.Ec.EcAccessException>();
    }
}
