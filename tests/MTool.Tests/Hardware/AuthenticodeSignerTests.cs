using MTool.App.Hardware;

namespace MTool.Tests.Hardware;

/// <summary>Reads files that are already on disk (the runtime and this test assembly); nothing is written.</summary>
public class AuthenticodeSignerTests
{
    [Fact]
    public void A_signed_runtime_file_names_its_signer()
    {
        var runtimeLibrary = typeof(object).Assembly.Location;

        AuthenticodeSigner.ReadSubject(runtimeLibrary).Should().Contain("Microsoft");
    }

    [Fact]
    public void An_unsigned_file_has_no_signer()
    {
        AuthenticodeSigner.ReadSubject(typeof(AuthenticodeSignerTests).Assembly.Location).Should().BeNull();
    }
}
