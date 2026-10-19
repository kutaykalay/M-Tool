using MTool.App.Cli;
using MTool.Tests.Fakes;

namespace MTool.Tests.Cli;

/// <summary>The words the command line says to the user come from the string resources, in the Windows language.</summary>
public sealed class CliTextTests
{
    private static readonly string NewLine = Environment.NewLine;

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void The_usage_names_every_command_in_every_language(string language)
    {
        using var _ = TestCulture.Use(language);

        var usage = CliText.Usage();

        usage.Should().Contain("--dump").And.Contain("--report [--wmi2]").And.Contain("--watch")
            .And.Contain("--apply").And.Contain("--restore").And.Contain("--unlock --confirm")
            .And.Contain("--tray").And.Contain("--confirm");
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void The_report_guidance_keeps_the_names_the_user_looks_for(string language)
    {
        using var _ = TestCulture.Use(language);

        CliText.ReportGuidance().Should().Contain("report.txt").And.Contain("dsdt.aml").And.Contain("Device report");
    }

    [Fact]
    public void The_Turkish_usage_is_byte_for_byte_the_old_text()
    {
        using var _ = TestCulture.Use("tr");

        CliText.Usage().Should().Be(
            $"Kullanım:{NewLine}  M-Tool.exe [--tray]   (GUI; --tray ile yalnızca tepside){NewLine}  M-Tool.exe --dump{NewLine}" +
            $"  M-Tool.exe --report [--wmi2]   (başka bir MSI modeli için rapor; yalnızca okur; --wmi2: yeni modellerde ham WMI2 paketleri){NewLine}" +
            $"  M-Tool.exe --watch [saniye]         (salt okuma, yalnızca WMI: 0xF2, 0xF4 ve fan tablolarındaki değişimleri kaydeder, varsayılan 60 sn, en çok 600){NewLine}" +
            "  M-Tool.exe --apply charge <50-100> [--confirm]\n" +
            "  M-Tool.exe --apply boost on|off [--confirm]\n" +
            "  M-Tool.exe --apply perf high|balanced|eco [--confirm]\n" +
            "  M-Tool.exe --apply fan default|cool|silent [--confirm]\n" +
            "  M-Tool.exe --apply fanmode auto|advanced [--confirm]\n" +
            "  M-Tool.exe --restore [--confirm]   (fabrika fan tablosu)\n" +
            "  M-Tool.exe --unlock --confirm      (başarısız yazmadan kalan kilidi kaldır)\n" +
            "  --confirm olmadan: dry-run, EC'ye yazılmaz. Komut satırı settings.json'daki dryRun'a bakmaz;\n" +
            "  her canlı yazma --confirm ister.");
    }

    [Fact]
    public void The_Turkish_messages_are_byte_for_byte_the_old_text()
    {
        using var _ = TestCulture.Use("tr");

        CliText.Saved(@"C:\x\dump.txt").Should().Be(@"Kaydedildi: C:\x\dump.txt");
        CliText.ReportGuidance().Should().Be(
            "Bu dosyayı GitHub'da \"Device report\" issue'suna ekleyin. Seri numarası sorgulanmadı, kullanıcı ve bilgisayar adı " +
            "report.txt'de gizlendi. dsdt.aml MSI'ın firmware tablosudur ve filtrelenmez. Göndermeden önce report.txt'ye göz atın.");
        CliText.NoWriteLock().Should().Be("Yazma kilidi yok.");
        CliText.LockRemoved("neden").Should().Be("Kilit kaldırıldı. Önceki neden: neden");
        CliText.Error("kötü").Should().Be("HATA: kötü");
        CliText.Details(@"C:\logs").Should().Be(@"Ayrıntı: C:\logs");
        CliText.DumpAfterWriteFailed("x").Should().Be("Yazma sonrası döküm alınamadı: x");
    }

    [Fact]
    public void The_English_messages_say_the_same_things()
    {
        using var _ = TestCulture.Use("en");

        CliText.Saved("p").Should().StartWith("Saved:").And.EndWith("p");
        CliText.NoWriteLock().Should().NotBeNullOrWhiteSpace().And.NotContain("Yazma");
        CliText.LockRemoved("why").Should().Contain("why");
        CliText.Error("bad").Should().Contain("bad").And.NotContain("HATA");
        CliText.Details("d").Should().Contain("d").And.NotContain("Ayrıntı");
        CliText.DumpAfterWriteFailed("x").Should().Be("Could not take the dump after writing: x");
    }

    [Fact]
    public void The_apply_and_watch_usage_follow_the_language()
    {
        using (TestCulture.Use("tr"))
        {
            ApplyCommand.Usage.Should().Contain("fabrika fan tablosu");
            WatchCommand.Usage.Should().Contain("salt okuma");
        }

        using (TestCulture.Use("en"))
        {
            ApplyCommand.Usage.Should().Contain("--restore").And.NotContain("fabrika");
            WatchCommand.Usage.Should().Contain("--watch").And.NotContain("salt okuma");
        }
    }
}
