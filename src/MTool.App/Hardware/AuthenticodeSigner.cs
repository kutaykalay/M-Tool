using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MTool.App.Hardware;

/// <summary>
/// Who signed a file, from its embedded Authenticode signature, parsed by Windows itself
/// (<c>CryptQueryObject</c>). Only the signer's certificate is read: the chain and trust are not
/// checked, because the question is "who", not "is it trusted" (MSI's own schema DLL is unsigned).
/// Catalog-signed files have no embedded signature and read as unsigned. Read-only.
/// </summary>
internal static class AuthenticodeSigner
{
    /// <summary>CRYPT_E_NO_MATCH: the file carries no signature.</summary>
    private const int NoSignature = unchecked((int)0x80092009);

    /// <summary>The subject of the signing certificate, or null when the file has no embedded signature.</summary>
    /// <exception cref="CryptographicException">The file could not be read or its signature is malformed.</exception>
    public static string? ReadSubject(string path)
    {
        try
        {
            // SYSLIB0057 points at X509CertificateLoader, which reads certificate files only, not the
            // signature inside a signed PE file; there is no replacement for this read.
#pragma warning disable SYSLIB0057
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return signer.Subject;
        }
        catch (CryptographicException ex) when (ex.HResult == NoSignature)
        {
            return null;
        }
    }
}
