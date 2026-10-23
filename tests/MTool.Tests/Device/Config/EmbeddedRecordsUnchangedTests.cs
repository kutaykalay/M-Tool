using System.Security.Cryptography;
using System.Text;
using MTool.Core.Device.Config;

namespace MTool.Tests.Device.Config;

/// <summary>
/// Plan asama15 §6: the WMI2 work leaves the three device records byte for byte as they are. A change
/// to one of them is a deliberate step that updates its hash here.
/// </summary>
public class EmbeddedRecordsUnchangedTests
{
    [Theory]
    [InlineData("msi-p65-creator-9se.json", "20097782285910ec9b41b917818910c091b8a19787f95a8f976035da097f0472")]
    [InlineData("msi-wmi1-generic.json", "0be4a14444c895063a6a143071157bd6f565d1235fe530a2c24a8eac08925878")]
    [InlineData("msi-wmi1-legacy.json", "31ae40c89cc13856f0cb8d06eb8dd85ce2b4522d1739a01352ebc0948f244113")]
    public void The_record_is_unchanged(string file, string sha256)
    {
        var assembly = typeof(DeviceConfigLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(DeviceConfigLoader.ResourcePrefix + file);
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!);

        // Line endings follow the checkout (CRLF on Windows, LF elsewhere); the content is what counts.
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);

        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Should().Be(sha256);
    }

    [Fact]
    public void There_are_exactly_three_records()
    {
        typeof(DeviceConfigLoader).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(DeviceConfigLoader.ResourcePrefix, StringComparison.Ordinal))
            .Should().HaveCount(3);
    }
}
