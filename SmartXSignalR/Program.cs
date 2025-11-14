using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SmartXSignalR.Hubs;
using SmartXSignalR.Options;
using SmartXSignalR.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SignalRHubOptions>(builder.Configuration.GetSection("SignalRHub"));
builder.Services.AddSingleton<HubState>();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB to accommodate block payloads
    options.StreamBufferCapacity = 64;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});

var issuer = builder.Configuration["Auth:Issuer"]
             ?? Environment.GetEnvironmentVariable("SIGNALR_JWT_ISSUER")
             ?? "smartXchain";
var signingKey = ResolveSigningKey(builder.Configuration, issuer);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/signalrhub"))
                    context.Token = accessToken;

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<SmartXHub>("/signalrhub").RequireAuthorization();

if (!app.Environment.IsProduction())
{
    app.MapGet("/token", ([FromQuery] string user) =>
    {
        if (string.IsNullOrWhiteSpace(user))
            return Results.BadRequest("Provide ?user=yourname");

        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: issuer,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user),
                new Claim(ClaimTypes.Name, user),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return Results.Ok(new { token = jwt });
    });
}

app.MapGet("/", () =>
    Results.Text("SmartX SignalR Hub is running. Visit /index.html for the diagnostics client.", "text/plain"));

app.Run();

static SymmetricSecurityKey ResolveSigningKey(IConfiguration configuration, string issuer)
{
    var base64Key = Environment.GetEnvironmentVariable("SMARTX_HMAC_B64")
                    ?? configuration["Auth:HmacKeyBase64"];

    if (string.IsNullOrWhiteSpace(base64Key))
    {
        var signingPassword = Environment.GetEnvironmentVariable("SIGNALR_PASSWORD")
                              ?? configuration["Auth:SigningPassword"];

        if (!string.IsNullOrWhiteSpace(signingPassword))
        {
            var derived = SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer}:{signingPassword}"));
            base64Key = Convert.ToBase64String(derived);
        }
    }

    if (string.IsNullOrWhiteSpace(base64Key))
    {
        var generated = RandomNumberGenerator.GetBytes(32);
        base64Key = Convert.ToBase64String(generated);
        Console.WriteLine(
            "WARNING: No signing key configured. Generated a volatile key. Configure SMARTX_HMAC_B64 or SIGNALR_PASSWORD for persistent tokens.");
    }

    byte[] keyBytes;
    try
    {
        keyBytes = Convert.FromBase64String(base64Key);
    }
    catch (FormatException ex)
    {
        throw new InvalidOperationException("Auth:HmacKeyBase64 / SMARTX_HMAC_B64 must be valid Base64.", ex);
    }

    if (keyBytes.Length < 32)
        throw new InvalidOperationException(
            $"HMAC key too short: {keyBytes.Length} bytes. Provide >= 32 bytes (256-bit) for HS256 tokens.");

    return new SymmetricSecurityKey(keyBytes);
}
