using System.Security.Cryptography;

namespace HackerOs.MarketPlace;

/// <summary>
/// Signatures of the catalog's packages (ADR 0214 §2 of Kazo-ca/HackerOs): ECDSA P-256 with
/// SHA-256 over the bytes of the <c>.hackapp</c>, written in IEEE P1363 form (<c>r||s</c>, 64 bytes)
/// as base64 in the <c>signature</c> field of each version. The OS checks it against the public key
/// it was built with (<c>HackerOsAppSigningKeys</c>) or that <c>[signing-keys]</c> lists.
/// </summary>
/// <remarks>
/// The private key is a PKCS#8 PEM file kept by the catalog's maintainer; the publication workflow
/// receives it through the <c>HACK_STORE_SIGNING_KEY</c> secret and never writes it to disk.
/// </remarks>
public static class PackageSigner
{
    /// <summary>Environment variable that holds the private key (PEM) during the publication.</summary>
    public const string KeyVariable = "HACK_STORE_SIGNING_KEY";

    /// <summary>Creates a new key pair.</summary>
    /// <returns>The private key as PKCS#8 PEM and the public key as base64 SubjectPublicKeyInfo.</returns>
    public static (string PrivateKeyPem, string PublicKey) CreateKeyPair()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Gets the public key of a private key.</summary>
    /// <param name="privateKeyPem">PKCS#8 PEM of a P-256 key.</param>
    /// <exception cref="ArgumentException">The key is not a P-256 private key.</exception>
    public static string GetPublicKey(string privateKeyPem)
    {
        using ECDsa key = Import(privateKeyPem);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Signs a package.</summary>
    /// <param name="package">Bytes of the <c>.hackapp</c>.</param>
    /// <param name="privateKeyPem">PKCS#8 PEM of a P-256 key.</param>
    /// <returns>The signature, base64.</returns>
    /// <exception cref="ArgumentException">The key is not a P-256 private key.</exception>
    public static string Sign(byte[] package, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(package);
        using ECDsa key = Import(privateKeyPem);
        return Convert.ToBase64String(key.SignData(package, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>Checks a signature the way the OS does.</summary>
    /// <param name="package">Bytes of the <c>.hackapp</c>.</param>
    /// <param name="signature">The base64 signature.</param>
    /// <param name="publicKey">The base64 SubjectPublicKeyInfo.</param>
    public static bool Verify(byte[] package, string signature, string publicKey)
    {
        ArgumentNullException.ThrowIfNull(package);
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.KeySize == 256
                && key.VerifyData(package, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static ECDsa Import(string privateKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportFromPem(privateKeyPem);
            if (key.KeySize != 256)
            {
                throw new ArgumentException("The signing key must be an ECDSA P-256 key.");
            }

            return key;
        }
        catch (CryptographicException exception)
        {
            key.Dispose();
            throw new ArgumentException($"The signing key is not a PKCS#8 PEM private key: {exception.Message}");
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
