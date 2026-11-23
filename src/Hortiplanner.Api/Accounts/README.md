# Data Protection keys

Hortiplanner stores its ASP.NET Core Data Protection keys in PostgreSQL. The keys protect session cookies and saved OpenRouter and OpenID Connect secrets. Keep the key ring with the database when restoring a deployment.

To encrypt new key-ring entries, mount a password-protected PKCS#12 file with an RSA private key where the application can read it. Set `DataProtection__CertificatePath` to its path inside the container. Set `DataProtection__CertificatePassword` in the deployment's secret store. Back up the certificate and its password separately from PostgreSQL and supply the same certificate on restore.

You can enable this on an existing deployment. The application can still read the old keys and the data they protect. ASP.NET Core encrypts newly generated keys with the certificate. Earlier entries remain as stored, so any that were written in plaintext are still readable from database backups. Keep the certificate available for as long as any entry encrypted with it is needed. [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0#protectkeyswithcertificate)
