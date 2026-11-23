using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hortiplanner.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Hortiplanner.Tests.AccountHarness;

namespace Hortiplanner.Tests;

public sealed class DataProtectionTests(PostgresFixture database)
{
    [Fact]
    public async Task A_deployment_certificate_encrypts_new_keys_and_preserves_legacy_payloads()
    {
        const string password = "test certificate password";
        const string value = "a test secret";
        const string oidcPurpose = "GrowLog.ExternalSignIn.ClientSecret";
        const string assistantPurpose = "GrowLog.Assistant.OpenRouterKey";
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Hortiplanner test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var now = DateTimeOffset.UtcNow;
        using var certificate = certificateRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
        var path = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.pfx");
        var connectionString = await database.CreateDatabaseAsync();

        TestApp App(string? certificatePath = null) => new(
            ("ConnectionStrings:Hortiplanner", connectionString),
            ("Passkeys:ServerDomain", "localhost"),
            ("Species:LineageBackfill", "false"),
            ("DataProtection:CertificatePath", certificatePath),
            ("DataProtection:CertificatePassword", password));

        try
        {
            await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pkcs12, password), Cancellation);
            await using var legacy = App();
            var oldPayload = legacy.Services.GetDataProtector(oidcPurpose).Protect(value);

            await using var configured = App(path);
            Assert.Equal(value, configured.Services.GetDataProtector(oidcPurpose).Unprotect(oldPayload));
            now = DateTimeOffset.UtcNow;
            var key = configured.Services.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));

            await using var writer = App(path);
            var newPayload = writer.Services.GetDataProtector(assistantPurpose).Protect(value);

            await using var db = database.CreateContext(connectionString);
            var row = await db.DataProtectionKeys.SingleAsync(item => item.FriendlyName == $"key-{key.KeyId}", Cancellation);
            Assert.Contains("encryptedSecret", row.Xml, StringComparison.Ordinal);
            Assert.Contains("EncryptedData", row.Xml, StringComparison.Ordinal);

            await using var restarted = App(path);
            Assert.Equal(value, restarted.Services.GetDataProtector(assistantPurpose).Unprotect(newPayload));
            Assert.Equal(value, restarted.Services.GetDataProtector(oidcPurpose).Unprotect(oldPayload));

            await using var withoutCertificate = App();
            Assert.Equal(value, withoutCertificate.Services.GetDataProtector(oidcPurpose).Unprotect(oldPayload));
            Assert.ThrowsAny<CryptographicException>(() => withoutCertificate.Services.GetDataProtector(assistantPurpose).Unprotect(newPayload));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
