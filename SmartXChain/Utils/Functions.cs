using System.Text.RegularExpressions;

namespace SmartXChain.Utils;

public static class Functions
{
    /// <summary>
    ///     Generates a new short GUID as a string.
    /// </summary>
    /// <returns>A short GUID string without dashes or braces, in uppercase.</returns>
    public static string NewGuid()
    {
        return Guid.NewGuid().ToString().ToUpper().Replace("-", "").Replace("{", "").Replace("}", "");
    }

    public static string AllowOnlyAlphanumeric(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var regex = new Regex("[^a-zA-Z0-9]");
        return regex.Replace(input, "");
    }
}