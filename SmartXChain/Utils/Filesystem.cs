// Decompiled with JetBrains decompiler
// Type: SmartXChain.Utils.FileSystem
// Assembly: SmartXChain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DECC2C05-B105-4F30-B07C-7BC7586D8D73
// Assembly location: C:\Users\Dell\Desktop\net8.0\SmartXChain.dll
// XML documentation location: C:\Users\Dell\Desktop\net8.0\SmartXChain.xml

namespace SmartXChain.Utils;

public static class FileSystem
{
    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException("ERROR: " + sourceDir + " not found.");
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
}