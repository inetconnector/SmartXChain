using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SmartXChain.Utils;

public class CertificateManager
{
    private readonly string appDirectory;
    private readonly string certificateFileName;
    private readonly string certificatePassword;
    private readonly string subjectName;

    public CertificateManager(string subjectName, string appDirectory, string certificateFileName,
        string certificatePassword)
    {
        this.subjectName = subjectName;
        this.appDirectory = appDirectory;
        this.certificateFileName = certificateFileName;
        this.certificatePassword = certificatePassword;
    }

    public X509Certificate2 GetCertificate()
    {

        var certPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDirectory,
            certificateFileName);

        if (!File.Exists(certPath)) throw new FileNotFoundException("Certificate file not found", certPath);

        return new X509Certificate2(certPath, certificatePassword);
    }

    public static X509Certificate2 GetCertificate(string certPath, string password="")
    { 
        if (!File.Exists(certPath)) throw new FileNotFoundException("Certificate file not found", certPath);

        return string.IsNullOrEmpty(password) ? new X509Certificate2(certPath) : 
            new X509Certificate2(certPath, password);
    }


    public string GenerateCertificate(string domainName, string country="DE")
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