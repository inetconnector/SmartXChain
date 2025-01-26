using System.IO.Compression;
using static SmartXChain.Utils.Config;

namespace SmartXChain.Utils;

public static class FileSystem
{
    private static string BlockchainPath
    {
        get
        {
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ChainName.ToString());

            var blockchainPath = Path.Combine(appDir, "Blockchain");
            Directory.CreateDirectory(blockchainPath);
            return blockchainPath;
        }
    }

    public static string WWWRoot
    {
        get
        {
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ChainName.ToString());

            var wwwroot = Path.Combine(appDir, "wwwroot");
            Directory.CreateDirectory(wwwroot);
            return wwwroot;
        }
    }

    public static string ContractsDir
    {
        get
        {
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ChainName.ToString());

            var contractsDir = Path.Combine(appDir, "Contracts");
            Directory.CreateDirectory(contractsDir);
            return contractsDir;
        }
    }

    public static string ConfigFile
    {
        get
        {
            var configFileName = ChainName == ChainNames.SmartXChain ? "config.txt" : "config.testnet.txt";

            // Ensure the AppData directory exists
            var appDirectory = AppDirectory;
            Directory.CreateDirectory(appDirectory);
            var config = Path.Combine(appDirectory, configFileName);
            return config;
        }
    }

    public static string AppDirectory
    {
        get
        {
            var chainName = ChainName.ToString();
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDir = Path.Combine(appDataPath, chainName);
            return appDir;
        }
    }

    public static string BlockchainDirectory
    {
        get
        {
            var blockchainDirectory = Path.Combine(AppDirectory, "Blockchain");
            Directory.CreateDirectory(blockchainDirectory); // Ensure directory exists

            return blockchainDirectory;
        }
    }

    /// <summary>
    ///     Creates a zip file from a given directory
    /// </summary>
    /// <param name="sourceDirectory"></param>
    /// <param name="zipFilePath"></param>
    /// <returns></returns>
    public static bool CreateZipFromDirectory(string sourceDirectory, string zipFilePath)
    {
        if (Directory.Exists(sourceDirectory))
        {
            if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

            ZipFile.CreateFromDirectory(sourceDirectory, zipFilePath);
            return true;
        }

        return false;
    }

    public static byte[] ReadZipFileAsBytes(string zipFilePath)
    {
        if (File.Exists(zipFilePath))
            // Lese die ZIP-Datei in ein Byte-Array
            return File.ReadAllBytes(zipFilePath);

        throw new FileNotFoundException($"ZIP-File {zipFilePath} not found.");
    }

    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            Logger.LogError(sourceDir + " not found.");
            throw new DirectoryNotFoundException("ERROR: " + sourceDir + " not found.");
        }

        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFileName = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFileName, true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var targetDir1 = Path.Combine(targetDir, Path.GetFileName(directory));
            CopyDirectory(directory, targetDir1);
        }
    }

    /// <summary>
    ///     Creates a backup of the current config and keys i.e. to ApplicationData\SmartXChain_Backup
    /// </summary>
    public static void CreateBackup()
    {
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ChainName.ToString());

        if (Directory.Exists(appDir))
        {
            if (Directory.Exists(WWWRoot))
                Directory.Delete(WWWRoot, true);

            if (Directory.Exists(BlockchainPath))
                Directory.Delete(BlockchainPath, true);

            CreateBackup(appDir);
            Directory.Delete(appDir, true);
        }
    }

    private static void CreateBackup(string appDir)
    {
        try
        {
            var tmp = Path.GetTempFileName();
            CreateZipFromDirectory(appDir, tmp);
            var backupBytes = ReadZipFileAsBytes(tmp);
            var backupDir = appDir + "_Backup";
            Directory.CreateDirectory(backupDir);
            var backupFile = Path.Combine(backupDir, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".zip");
            File.WriteAllBytes(backupFile, backupBytes);
            File.Delete(tmp);
            Logger.Log($"Backup created: {backupFile}.");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Saving Backup failed");
        }
    }
}