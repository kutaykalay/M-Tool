using MTool.Core.Device.Config;
using MTool.Core.Ec;
using MTool.Core.Localization;
using MTool.Core.Profiles;
using MTool.Core.Resources;
using MTool.Tests.Device.Config;
using MTool.Tests.Fakes;

namespace MTool.Tests.Ec;

/// <summary>
/// The texts of the write rules, the port guard, the record's limit check and the unknown-profile error in Turkish,
/// English and a language M-Tool lacks (Finnish, which falls back to English). The Turkish texts are the ones from before
/// the move to resources; the log twin is always English.
/// </summary>
public sealed class WriteRuleTextCultureTests
{
    public static TheoryData<string, byte, byte, string, string> StaticCases => new()
    {
        { "not on the whitelist", 0xF5, 0x83, "0xF5 yazılabilir register listesinde değil.", "0xF5 is not in the list of writable registers." },
        { "up threshold low", 0x6A, 29, "0x6A=0x1D: yukarı eşik 30-95 °C olmalı.", "0x6A=0x1D: the up threshold must be 30-95 °C." },
        { "up threshold high", 0x6A, 96, "0x6A=0x60: yukarı eşik 30-95 °C olmalı.", "0x6A=0x60: the up threshold must be 30-95 °C." },
        { "speed", 0x72, 101, "0x72=0x65: fan hızı en fazla %100.", "0x72=0x65: the fan speed is at most 100%." },
        { "charge limit", 0xEF, 0x80 | 49, "0xEF=0xB1: şarj limiti 0x80 | %50-100 olmalı.", "0xEF=0xB1: the charge limit must be 0x80 | 50-100%." },
        { "performance", 0xF2, 0xC3, "0xF2=0xC3: desteklenmeyen performans modu.", "0xF2=0xC3: unsupported performance mode." },
        { "fan mode", 0xF4, 0x0E, "0xF4=0x0E: desteklenmeyen fan modu.", "0xF4=0x0E: unsupported fan mode." },
        { "keyboard backlight", 0xF3, 0x84, "0xF3=0x84: desteklenmeyen klavye ışığı kademesi.", "0xF3=0x84: unsupported keyboard backlight level." },
    };

    [Theory]
    [MemberData(nameof(StaticCases))]
    public void A_static_rule_is_in_the_language_of_the_user_and_its_log_twin_in_English(
        string name, byte register, byte value, string turkish, string english)
    {
        var write = new RegisterWrite(register, value);

        using (TestCulture.Use("tr"))
        {
            var text = EcWriteRules.CheckStaticText(write)!;
            text.Text.Should().Be(turkish, name);
            text.Log.Should().Be(english, name);
            EcWriteRules.CheckStatic(write).Should().Be(turkish, name);
        }

        foreach (var culture in new[] { "en", "fi" })
        {
            using var _ = TestCulture.Use(culture);
            var text = EcWriteRules.CheckStaticText(write)!;
            text.Text.Should().Be(english, name);
            text.Log.Should().Be(english, name);
        }
    }

    [Fact]
    public void A_cooler_boost_rule_names_the_current_value_in_every_language()
    {
        var write = new RegisterWrite(0x98, 0x80);

        using (TestCulture.Use("tr"))
        {
            var text = EcWriteRules.CheckAgainstCurrentText(write, 0x02)!;
            text.Text.Should().Be("0x98=0x80: Cooler Boost yazmasında yalnızca bit 7 değişebilir (şu an 0x02).");
            text.Log.Should().Be("0x98=0x80: a Cooler Boost write may change only bit 7 (currently 0x02).");
        }

        using (TestCulture.Use("fi"))
        {
            EcWriteRules.CheckAgainstCurrentText(write, 0x02)!.Text
                .Should().Be("0x98=0x80: a Cooler Boost write may change only bit 7 (currently 0x02).");
        }
    }

    [Fact]
    public void Writes_that_pass_the_rules_have_no_text()
    {
        using var _ = TestCulture.Use("en");

        EcWriteRules.CheckStaticText(new RegisterWrite(0x6A, 30)).Should().BeNull();
        EcWriteRules.CheckAgainstCurrentText(new RegisterWrite(0x98, 0x82), 0x02).Should().BeNull();
        EcWriteRules.CheckAgainstCurrentText(new RegisterWrite(0x72, 5), 0x02).Should().BeNull();
    }

    [Fact]
    public void The_port_guard_texts_follow_the_language()
    {
        var changed = new PortDisturbance([new RegisterWrite(0xF2, 0xC4), new RegisterWrite(0x6A, 0x32)], Unreadable: false);
        var unreadable = new PortDisturbance([], Unreadable: true);
        var none = new PortDisturbance([], Unreadable: false);

        using (TestCulture.Use("tr"))
        {
            changed.Describe().Text.Should().Be("Port erişimi başka register'ları değiştirdi: 0xF2=0xC4 0x6A=0x32.");
            changed.Describe().Log.Should().Be("The port access changed other registers: 0xF2=0xC4 0x6A=0x32.");
            unreadable.Describe().Text.Should().Be("İzlenen register'lar port erişiminden sonra okunamadı.");
            unreadable.Describe().Log.Should().Be("The watched registers could not be read after the port access.");
            none.Describe().Should().Be(new UserAndLogText("", ""));
        }

        using (TestCulture.Use("fi"))
        {
            changed.Describe().Text.Should().Be("The port access changed other registers: 0xF2=0xC4 0x6A=0x32.");
            unreadable.Describe().Text.Should().Be("The watched registers could not be read after the port access.");
        }
    }

    [Fact]
    public void A_device_record_problem_is_reported_in_english_whatever_the_language()
    {
        // The record check is for whoever adds a record and for the log, not for the user.
        var outside = DeviceConfigFixtures.P65() with { Limits = new CurveLimits(30, 85, 100) };
        var cpu = DeviceConfigFixtures.CpuFan() with { FactoryDownOffsets = [8, 3, 3, 3, 3, 10] };
        var failing = DeviceConfigFixtures.P65() with { Fans = [cpu, DeviceConfigFixtures.GpuFan()] };

        using (TestCulture.Use("tr"))
        {
            DeviceConfigValidator.Validate(outside).Should().Contain("cpu factory curve: outside the limits of the record (30-85 °C, at most 100%).");
            DeviceConfigValidator.Validate(failing).Should().ContainMatch("cpu factory curve: Point 7: the fan drops back from this point at 78 °C*");
        }
    }

    [Fact]
    public void An_unknown_profile_is_reported_in_the_language_of_the_user()
    {
        var plan = () => new DesiredState("Turbo").ToPlans(ProfileCatalog.BuiltIn);

        using (TestCulture.Use("tr"))
        {
            plan.Should().Throw<ArgumentException>().Which.Message.Should().StartWith("Bilinmeyen fan profili: Turbo");
        }

        using (TestCulture.Use("en"))
        {
            plan.Should().Throw<ArgumentException>().Which.Message.Should().StartWith("Unknown fan profile: Turbo");
        }
    }

    [Fact]
    public void A_failed_lookup_never_makes_a_rule_check_throw()
    {
        UserAndLogText.LookupFault.Value = key => throw new InvalidOperationException($"no resource {key}");
        try
        {
            var text = EcWriteRules.CheckStaticText(new RegisterWrite(0xF5, 0x83));
            var current = EcWriteRules.CheckAgainstCurrentText(new RegisterWrite(0x98, 0x80), 0x02);
            var guard = new PortDisturbance([new RegisterWrite(0xF2, 0xC4)], Unreadable: false).Describe();
            var plan = () => new DesiredState("Turbo").ToPlans(ProfileCatalog.BuiltIn);

            text.Should().NotBeNull();
            text!.Log.Should().StartWith(nameof(CoreStrings.Rule_NotWritable));
            current.Should().NotBeNull();
            guard.Text.Should().StartWith(nameof(CoreStrings.PortGuard_Changed));
            plan.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(nameof(CoreStrings.Profile_Unknown));
        }
        finally
        {
            UserAndLogText.LookupFault.Value = null;
        }
    }
}
