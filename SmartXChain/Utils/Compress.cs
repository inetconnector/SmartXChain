using System.IO.Compression;
using System.Text;

namespace SmartXChain.Utils;

/// <summary>
///     Provides utility methods for compressing and decompressing data using GZip compression.
/// </summary>
public static class Compress
{
    /// <summary>
    ///     Compresses a string into a GZip-compressed byte array.
    /// </summary>
    /// <param name="data">The string data to compress.</param>
    /// <returns>A byte array containing the compressed data.</returns>
    public static byte[] CompressString(string data)
    {
        using (var output = new MemoryStream())
        {
            // Create a GZipStream to compress data
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            using (var writer = new StreamWriter(gzip, Encoding.UTF8))
            {
                writer.Write(data); // Write the string data into the compression stream
            }

            return output.ToArray(); // Return the compressed data as a byte array
        }
    }

    /// <summary>
    ///     Decompresses a GZip-compressed byte array into a string.
    /// </summary>
    /// <param name="compressedData">The byte array containing compressed data.</param>
    /// <returns>The decompressed string.</returns>
    public static string DecompressString(byte[] compressedData)
    {
        using (var input = new MemoryStream(compressedData))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip, Encoding.UTF8))
        {
            return reader.ReadToEnd(); // Read and return the decompressed string
        }
    }
}