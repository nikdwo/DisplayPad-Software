using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DisplayPad.Shared.Models;

namespace DisplayPad.Agent;

public sealed record AgentCertificate(X509Certificate2 Certificate, string Fingerprint);

public static class AgentCertificateStore
{
    public static AgentCertificate LoadOrCreate(AgentConfig config)
    {
        if (File.Exists(AgentConfig.CertificatePath))
        {
            if (string.IsNullOrWhiteSpace(config.CertificatePasswordProtected))
                throw new AgentConfigException("CertificatePasswordMissing");
            var existingPassword = SecretProtector.Unprotect(config.CertificatePasswordProtected);
            var existing = new X509Certificate2(AgentConfig.CertificatePath, existingPassword,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            return new AgentCertificate(existing, Fingerprint(existing));
        }

        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=DisplayPad Agent", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var generatedPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllBytes(AgentConfig.CertificatePath, generated.Export(X509ContentType.Pfx, generatedPassword));
        config.CertificatePasswordProtected = SecretProtector.Protect(generatedPassword);
        AgentConfig.Save(config);

        var loaded = new X509Certificate2(AgentConfig.CertificatePath, generatedPassword,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        return new AgentCertificate(loaded, Fingerprint(loaded));
    }

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
