using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SmartXChain.Utils;

public static class BearerToken
{
    internal static string GetToken()
    {
        var token = "";
        try
        {
            token = GenerateToken(Crypt.AssemblyFingerprint.Substring(0, 40), Config.Default.MinerAddress);
            Logger.Log("Generated Bearer Token: " + token);
        }
        catch (Exception ex)
        {
            Logger.Log($"ERROR: GenerateToken failed: {ex.Message}");
        }

        return token;
    }

    private static string GenerateToken(string secretKey, string userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            "your-app",
            "your-audience",
            claims,
            expires: DateTime.UtcNow.AddSeconds(30),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}