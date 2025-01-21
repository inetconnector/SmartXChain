// Decompiled with JetBrains decompiler
// Type: SmartXChain.Utils.FileSystem
// Assembly: SmartXChain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DECC2C05-B105-4F30-B07C-7BC7586D8D73
// Assembly location: C:\Users\Dell\Desktop\net8.0\SmartXChain.dll
// XML documentation location: C:\Users\Dell\Desktop\net8.0\SmartXChain.xml

using System.IO.Compression;

namespace SmartXChain.Utils;

public static class FileSystem
{
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

        throw new FileNotFoundException($"Die ZIP-Datei {zipFilePath} wurde nicht gefunden.");
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
            "SmartXChain_Testnet");

        if (Directory.Exists(appDir))
        {
            var path = Path.Combine(appDir, "Blockchain");
            if (Directory.Exists(path))
                Directory.Delete(path, true);

            path = Path.Combine(appDir, "wwwroot");
            if (Directory.Exists(path))
                Directory.Delete(path, true);

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