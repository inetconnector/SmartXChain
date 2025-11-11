using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SmartXChain.Utils;

public static class BearerToken
{
    internal static string GetToken()
    {
        try
        {
            var token = GenerateToken(Crypt.AssemblyFingerprint, Config.Default.MinerAddress);
            Logger.Log("Generated bearer token for miner address.");
            return token;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "GenerateToken failed");
            return string.Empty;
        }
    }

    private static string GenerateToken(string secretKeyMaterial, string userId)
    {
        if (string.IsNullOrWhiteSpace(secretKeyMaterial))
            throw new ArgumentException("Secret key material is missing.", nameof(secretKeyMaterial));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is missing.", nameof(userId));

        var derivedKey = DeriveSigningKey(secretKeyMaterial);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(derivedKey), SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "your-app",
            audience: "your-audience",
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(30),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] DeriveSigningKey(string secretKeyMaterial)
    {
        try
        {
            var rawKey = Convert.FromBase64String(secretKeyMaterial);
            if (rawKey.Length >= 32)
                return rawKey.Take(32).ToArray();

            return SHA256.HashData(rawKey);
        }
        catch (FormatException)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(secretKeyMaterial));
        }
    }
}
