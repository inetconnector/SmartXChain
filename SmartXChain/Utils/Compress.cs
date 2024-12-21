using System.IO.Compression;
using System.Text;

public static class Compress
{
    // Helper method to compress a string
    public static byte[] CompressString(string data)
    {
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            using (var writer = new StreamWriter(gzip, Encoding.UTF8))
            {
                writer.Write(data);
            }

            return output.ToArray();
        }
    }

    // Helper method to decompress a byte array into a string
    public static string DecompressString(byte[] compressedData)
    {
        using (var input = new MemoryStream(compressedData))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }
}