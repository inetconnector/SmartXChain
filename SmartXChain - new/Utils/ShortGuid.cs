namespace SmartXChain.Utils;

public static class ShortGuid
{
    /// <summary>
    ///     Generates a new short GUID as a string.
    /// </summary>
    /// <returns>A short GUID string without dashes or braces, in uppercase.</returns>
    public static string NewGuid()
    {
        return Guid.NewGuid().ToString().ToUpper().Replace("-", "").Replace("{", "").Replace("}", "");
    }
}