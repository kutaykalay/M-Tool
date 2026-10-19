using MTool.Core.Device;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// A WMI answer that cannot be right reaches the user inside the "could not read" band, so it follows the UI
/// language and the log gets the English twin. A routing mistake is a programming error: English only.
/// </summary>
public sealed class RoutedEcRegistersCultureTests
{
    private static EcAccessException OutOfRangeIn(string culture)
    {
        var wmi = new FakeWmiFields(P65Memory.FactorySnapshot())
        {
            ReadHook = field => field == new WmiField(WmiMap.CpuClass, 1) ? 300 : null,
        };
        var routed = new RoutedEcRegisters(wmi, port: null, TestLayouts.P65.Wmi);
        using var _ = TestCulture.Use(culture);

        return Assert.Throws<EcAccessException>(() => routed.Read(0x68));
    }

    private static EcAccessException ShortAnswerIn(string culture)
    {
        var routed = new RoutedEcRegisters(new OneFieldWmi(), port: null, TestLayouts.P65.Wmi);
        using var _ = TestCulture.Use(culture);

        return Assert.Throws<EcAccessException>(() => routed.ReadBlock(0x6A, 6));
    }

    [Fact]
    public void A_value_that_does_not_fit_a_byte_is_in_the_language_of_the_user_and_English_in_the_log()
    {
        var turkish = OutOfRangeIn("tr");
        var english = OutOfRangeIn("en");

        turkish.Message.Should().Be("WMI alanı MSI_CPU[1] bir bayta sığmayan 300 değerini döndürdü.");
        english.Message.Should().Be("WMI field MSI_CPU[1] returned 300, which does not fit in a byte.");
        turkish.ForLog().Should().Be(english.Message);
    }

    [Fact]
    public void A_wrong_field_count_is_in_the_language_of_the_user_and_English_in_the_log()
    {
        var turkish = ShortAnswerIn("tr");
        var english = ShortAnswerIn("en");

        turkish.Message.Should().Be("WMI MSI_CPU: 6 alan istendi, 1 geldi.");
        english.Message.Should().Be("WMI MSI_CPU returned 1 fields; 6 were asked for.");
        turkish.ForLog().Should().Be(english.Message);
    }

    [Fact]
    public void A_register_that_cannot_be_routed_is_an_English_programming_error()
    {
        var routed = new RoutedEcRegisters(new FakeWmiFields(P65Memory.FactorySnapshot()), port: null, TestLayouts.P65.Wmi);
        using var _ = TestCulture.Use("tr");

        ((Action)(() => routed.Read(0x55))).Should().Throw<InvalidOperationException>()
            .WithMessage("0x55 is not in the WMI map and not allowed on the port.");
        ((Action)(() => routed.Read(0x98))).Should().Throw<InvalidOperationException>()
            .WithMessage("0x98 can only be reached through the port, and the port is closed.");
    }

    private sealed class OneFieldWmi : IWmiFields
    {
        public IReadOnlyList<int> Read(string className, IReadOnlyList<int> indices) => [0];

        public void Write(string className, int index, byte value) => throw new NotSupportedException();
    }
}
