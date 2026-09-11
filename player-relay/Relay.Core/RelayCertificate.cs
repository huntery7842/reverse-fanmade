using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ReVerse.Relay;

public static class RelayCertificate
{
    public static X509Certificate2 LoadOrCreate(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "localhost.pfx");
        if (File.Exists(path))
        {
            var existing = X509CertificateLoader.LoadPkcs12FromFile(path, null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            if (existing.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)) return existing;
            existing.Dispose();
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=ReVerse Local Relay", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
        File.WriteAllBytes(Path.Combine(directory, "localhost.cer"), cert.Export(X509ContentType.Cert));
        return X509CertificateLoader.LoadPkcs12FromFile(path, null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
    }
}
