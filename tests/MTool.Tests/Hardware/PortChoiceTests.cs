using MTool.App.Hardware;
using MTool.App.Resources;
using MTool.Core.Ec;

namespace MTool.Tests.Hardware;

public sealed class PortChoiceTests
{
    private static bool Installed() => true;

    private static bool Missing() => false;

    private static bool NotAsked() => throw new InvalidOperationException("PawnIO must not be looked up here.");

    [Theory]
    [InlineData(nameof(EcBackends.Hybrid))]
    [InlineData(nameof(EcBackends.PreferHybrid))]
    public void A_verified_firmware_with_PawnIO_uses_the_port_without_a_warning(string backendName)
    {
        var backend = Enum.Parse<EcBackends>(backendName);
        var choice = PortChoice.For(backend, portAllowed: true, Installed);

        choice.Should().Be(new PortChoice(UsePort: true, PortFallback.None));
        choice.Warnings(firmwareRead: true, backend).Should().BeEmpty();
        choice.LogSuffix.Should().BeEmpty();
    }

    [Fact]
    public void Hybrid_without_PawnIO_still_stops_with_the_install_command()
    {
        var choose = () => PortChoice.For(EcBackends.Hybrid, portAllowed: true, Missing);

        choose.Should().Throw<PawnIoMissingException>();
    }

    [Fact]
    public void PreferHybrid_without_PawnIO_opens_WMI_only_and_says_how_to_get_the_rest()
    {
        var choice = PortChoice.For(EcBackends.PreferHybrid, portAllowed: true, Missing);

        choice.Should().Be(new PortChoice(UsePort: false, PortFallback.PawnIoMissing));
        choice.Warnings(firmwareRead: true, EcBackends.PreferHybrid).Should().Equal(
            string.Format(Strings.Session_PawnIoMissing, PawnIoInstallation.InstallCommand));
        choice.LogSuffix.Should().Be(" (PawnIO not installed: port closed)");
    }

    [Theory]
    [InlineData(nameof(EcBackends.Hybrid))]
    [InlineData(nameof(EcBackends.PreferHybrid))]
    public void An_unverified_firmware_closes_the_port_without_looking_for_PawnIO(string backendName)
    {
        var backend = Enum.Parse<EcBackends>(backendName);
        var choice = PortChoice.For(backend, portAllowed: false, NotAsked);

        choice.Should().Be(new PortChoice(UsePort: false, PortFallback.UnverifiedFirmware));
        choice.Warnings(firmwareRead: true, backend).Should().BeEmpty();
        choice.LogSuffix.Should().Be(" (unverified firmware: port closed)");
    }

    [Theory]
    [InlineData(nameof(EcBackends.Hybrid))]
    [InlineData(nameof(EcBackends.PreferHybrid))]
    public void An_unread_firmware_is_a_warning_when_the_port_was_wanted(string backendName)
    {
        var backend = Enum.Parse<EcBackends>(backendName);
        var choice = PortChoice.For(backend, portAllowed: false, NotAsked);

        choice.Warnings(firmwareRead: false, backend).Should().Equal(Strings.Session_FirmwareUnread);
    }

    [Fact]
    public void WmiOnly_never_looks_for_PawnIO_and_has_nothing_to_warn_about()
    {
        var choice = PortChoice.For(EcBackends.WmiOnly, portAllowed: true, NotAsked);

        choice.Should().Be(new PortChoice(UsePort: false, PortFallback.None));
        choice.Warnings(firmwareRead: false, EcBackends.WmiOnly).Should().BeEmpty();
        choice.LogSuffix.Should().BeEmpty();
    }
}
