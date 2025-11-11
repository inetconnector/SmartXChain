using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SmartXChain.Utils;

/// <summary>
///     Manages X.509 certificates for the SmartXChain node, including creation, retrieval, and installation helpers.
/// </summary>
public class CertificateManager
{
    private readonly string appDirectory;
    private readonly string certificateFileName;
    private readonly string certificatePassword;
    private readonly string subjectName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CertificateManager" /> class with the given certificate metadata.
    /// </summary>
    /// <param name="subjectName">The subject name that should be embedded in generated certificates.</param>
    /// <param name="appDirectory">Application directory used to persist certificate files.</param>
    /// <param name="certificateFileName">Filename for the persisted certificate.</param>
    /// <param name="certificatePassword">Password used to protect the certificate on disk.</param>
    public CertificateManager(string subjectName, string appDirectory, string certificateFileName,
        string certificatePassword)
    {
        this.subjectName = subjectName;
        this.appDirectory = appDirectory;
        this.certificateFileName = certificateFileName;
        this.certificatePassword = certificatePassword;
    }

    /// <summary>
    ///     Retrieves the configured certificate from the application data directory.
    /// </summary>
    /// <returns>The loaded certificate.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the certificate file does not exist.</exception>
    public X509Certificate2 GetCertificate()
    {
        var certPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDirectory,
            certificateFileName);

        if (!File.Exists(certPath)) throw new FileNotFoundException("Certificate file not found", certPath);

        return new X509Certificate2(certPath, certificatePassword);
    }

    /// <summary>
    ///     Loads a certificate from the specified path using the optional password.
    /// </summary>
    /// <param name="certPath">The absolute path to the certificate file.</param>
    /// <param name="password">Optional password for the certificate.</param>
    /// <returns>The loaded certificate.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the certificate file does not exist.</exception>
    public static X509Certificate2 GetCertificate(string certPath, string password = "")
    {
        if (!File.Exists(certPath)) throw new FileNotFoundException("Certificate file not found", certPath);

        return string.IsNullOrEmpty(password)
            ? new X509Certificate2(certPath)
            : new X509Certificate2(certPath, password);
    }


    /// <summary>
    ///     Generates and persists a self-signed certificate for the specified domain if it does not yet exist.
    /// </summary>
    /// <param name="domainName">The domain name to include in the certificate subject.</param>
    /// <param name="country">Two-letter ISO country code used in the certificate subject.</param>
    /// <returns>The path to the generated or existing certificate file.</returns>
    public string GenerateCertificate(string domainName, string country = "DE")
    {
        var certPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDirectory,
            certificateFileName);
        if (File.Exists(certPath)) return certPath;
        var validSubjectName = $"CN={domainName}, O={subjectName}, C={country.ToUpper()}";
        var ecdsa = ECDsa.Create();
        var req = new CertificateRequest(validSubjectName, ecdsa, HashAlgorithmName.SHA256);
        var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(10));

        // Export certificate in PFX format 
        File.WriteAllBytes(certPath, cert.Export(X509ContentType.Pfx, certificatePassword));

        return certPath;
    }

    /// <summary>
    ///     Determines whether a certificate containing the configured subject is installed in the local root store.
    /// </summary>
    /// <returns><c>true</c> if a matching certificate exists; otherwise, <c>false</c>.</returns>
    public bool IsCertificateInstalled()
    {
        //var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        try
        {
            foreach (var cert in store.Certificates)
                if (cert.Subject.Contains(subjectName))
                    return true;
        }
        finally
        {
            store.Close();
        }

        return false;
    }

    /// <summary>
    ///     Installs the specified certificate into the local machine root store using elevated privileges.
    /// </summary>
    /// <param name="certPath">The path to the certificate file to install.</param>
    public void InstallCertificate(string certPath)
    {
        //own certs
        var arguments = $"-f -p \"{certificatePassword}\" -importpfx \"{certPath}\"";

        RunAsAdmin("certutil.exe", arguments);
    }

    private void RunAsAdmin(string fileName, string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            Verb = "runas", // Run as administrator
            UseShellExecute = true,
            CreateNoWindow = true
        };

        try
        {
            using (var process = Process.Start(processInfo))
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception($"Process exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to execute command as admin: {ex.Message}");
        }
    }
}