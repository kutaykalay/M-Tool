using System.Security.Cryptography;
using MTool.App.Hardware;

namespace MTool.Tests.Hardware;

/// <summary>Reads files that are already on disk (the runtime and this test assembly) and two throwaway files in the temp folder.</summary>
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

    [Fact]
    public void An_empty_file_throws_instead_of_reading_as_unsigned()
    {
        var act = () => ReadSubjectOfTempFile([]);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_text_file_has_no_signature_like_any_unsigned_file()
    {
        ReadSubjectOfTempFile("not a PE file"u8.ToArray()).Should().BeNull();
    }

    /// <summary>The only write of these tests: a small file under the temp folder, deleted again.</summary>
    private static string? ReadSubjectOfTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtool-signer-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, content);
        try
        {
            return AuthenticodeSigner.ReadSubject(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
