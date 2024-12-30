using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SmartXChain.Utils;

/// <summary>
///     Provides serialization utilities for compressing and decompressing objects using Base64 encoding and GZip
///     compression.
/// </summary>
public static class Serializer
{
    /// <summary>
    ///     Serializes an object to a Base64 string with GZip compression.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="instance">The instance of the object to serialize.</param>
    /// <returns>A Base64-encoded string representing the compressed JSON of the object.</returns>
    public static string SerializeToBase64<T>(T instance)
    {
        // Convert the object to a JSON string
        var json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });

        // Compress the JSON string and encode it as Base64
        using (var memoryStream = new MemoryStream())
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
            gzipStream.Close(); // Complete the compression
            return Convert.ToBase64String(memoryStream.ToArray());
        }
    }

    /// <summary>
    ///     Deserializes a Base64 string with GZip compression back into an object.
    /// </summary>
    /// <typeparam name="T">The type of the object to deserialize into.</typeparam>
    /// <param name="base64Data">The Base64-encoded string representing the compressed JSON.</param>
    /// <returns>The deserialized object of type <typeparamref name="T" />.</returns>
    public static T DeserializeFromBase64<T>(string base64Data) where T : class
    {
        // Decode the Base64 string into a byte array
        var compressedData = Convert.FromBase64String(base64Data);

        // Decompress the data and deserialize it into the specified object type
        using (var memoryStream = new MemoryStream(compressedData))
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
        {
            var json = reader.ReadToEnd(); // Read the decompressed JSON string
            return JsonSerializer.Deserialize<T>(json)!; // Deserialize the JSON into an object
        }
    }
}