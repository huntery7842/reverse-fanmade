using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ReVerse.Capture.Configuration;

public static class LocalCertificate
{
    private const string Password = "local-cert";

    public static X509Certificate2 GetOrCreate()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(directory);
        var pfxPath = Path.Combine(directory, "local-cert.pfx");
        if (File.Exists(pfxPath))
            return LoadPersisted(pfxPath);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, Password));
        return LoadPersisted(pfxPath);
    }

    private static X509Certificate2 LoadPersisted(string pfxPath) =>
        X509CertificateLoader.LoadPkcs12FromFile(
            pfxPath,
            Password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
}
