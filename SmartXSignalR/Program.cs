using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// === Auth Key Loading (prefer ENV) ===
string? b64 = Environment.GetEnvironmentVariable("SMARTX_HMAC_B64")
              ?? builder.Configuration["Auth:HmacKeyBase64"];
byte[] keyBytes;
if (string.IsNullOrWhiteSpace(b64))
{
    // Fallback: generate volatile key (for local test). NOTE: App restarts will invalidate old tokens.
    keyBytes = RandomNumberGenerator.GetBytes(32);
    Console.WriteLine("WARNING: No HMAC key provided. Generated a volatile key. Set SMARTX_HMAC_B64 in environment or Auth:HmacKeyBase64 in appsettings.json");
}
else
{
    try
    {
        keyBytes = Convert.FromBase64String(b64);
    }
    catch
    {
        throw new InvalidOperationException("Auth:HmacKeyBase64 / SMARTX_HMAC_B64 is not valid Base64.");
    }
}
if (keyBytes.Length < 32) // 256-bit recommended
    throw new InvalidOperationException($"HMAC key too short: {keyBytes.Length} bytes. Provide >= 16 bytes (128-bit), ideally 32 bytes (256-bit).");

var signingKey = new SymmetricSecurityKey(keyBytes);

// Services
builder.Services.AddSignalR();
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
        p.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true));
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Allow SignalR to send token via query string `access_token` on WebSockets/ServerSentEvents
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/signalrhub"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Behind IIS / reverse proxy
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

app.MapHub<SmartXHub>("/signalrhub").RequireAuthorization(); // secured hub

// Minimal test token endpoint (REMOVE FOR PRODUCTION)
app.MapGet("/token", ([FromQuery] string user) =>
{
    if (string.IsNullOrWhiteSpace(user))
        return Results.BadRequest("Provide ?user=yourname");

    var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        claims: new[]
        {
            new Claim(ClaimTypes.Name, user),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        },
        notBefore: DateTime.UtcNow,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: creds
    );
    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { token = jwt });
});

// Root info
app.MapGet("/", () => Results.Text("SmartX SignalR Hub is running. Visit /index.html for client test.", "text/plain"));

app.Run();

public class SmartXHub : Hub
{
    public async Task Echo(string message)
    {
        var name = Context.User?.Identity?.Name ?? "anonymous";
        await Clients.Caller.SendAsync("echo", $"{name}: {message}");
    }

    public async Task Broadcast(string message)
    {
        var name = Context.User?.Identity?.Name ?? "anonymous";
        await Clients.All.SendAsync("broadcast", $"{name}: {message}");
    }

    public Task JoinGroup(string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);
    public Task LeaveGroup(string group) => Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    public Task SendToGroup(string group, string message)
    {
        var name = Context.User?.Identity?.Name ?? "anonymous";
        return Clients.Group(group).SendAsync("group", group, $"{name}: {message}");
    }
}
