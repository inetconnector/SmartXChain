# SmartX SignalR Hub (Plesk, Windows/IIS)

Ready for deployment under **Plesk (Windows)** as an ASP.NET Core app.
Includes: - Secured **SignalR Hub** under `/signalrhub` (JWT Bearer,
HS256) - `web.config` for IIS/Plesk including **WebSockets** - Test page
at `/index.html` - Test token endpoint `/token?user=Name` (for testing
only; remove in production)

## 1) Requirements

-   Install the **.NET 8 Hosting Bundle** on your Windows server
    (restart afterward).
-   In Plesk: enable SSL/TLS (Let's Encrypt).

## 2) Deployment (Plesk)

-   Use a new or existing domain/subdomain, e.g.,
    signalr.yourdomain.tld.
-   Folder (document root) e.g., `httpdocs\signalr`.
-   Upload **all files** from this ZIP into the folder.
-   No compilation needed on the server -- it's a framework-dependent
    app.

## 3) Set secret (HMAC key)

Best set as an **environment variable** in Plesk/IIS: - Name:
`SMARTX_HMAC_B64` - Value: 32 random bytes, Base64 encoded.

### Generate token (PowerShell on Windows):

``` powershell
$bytes = New-Object 'System.Byte[]' 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

Alternatively, set `Auth:HmacKeyBase64` in `appsettings.json` (not
recommended in repo).

## 4) Enable WebSockets

-   In Windows Server roles: enable **WebSocket Protocol**.
-   In `web.config`, `<webSocket enabled="true" />` is already
    configured.

## 5) Start & test

-   Browser: `https://yourdomain.tld/index.html`
-   Get test token: `https://yourdomain.tld/token?user=Daniel`
-   Paste token into the page → **Connect**.
-   Test messages using **Echo** or **Broadcast**.

> Note: `/token` is for testing only. Please remove or protect it in
> production.

## 6) Client configuration (C#)

``` csharp
var jwt = "YOUR_TOKEN"; // e.g., from your login flow
var connection = new HubConnectionBuilder()
    .WithUrl("https://yourdomain.tld/signalrhub", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(jwt);
    })
    .WithAutomaticReconnect()
    .Build();

await connection.StartAsync();
```

## 7) Common errors

-   **502.5 Process Failure** → .NET Hosting Bundle missing or wrong
    version.
-   **WebSocket connection failed** → Role not installed or disabled by
    hoster.
-   **IDX10653 Key too small** → HMAC key \< 16 bytes. Use 32 bytes.
-   **CORS error** → Adjust CORS in `Program.cs` (use whitelist instead
    of `AllowAny...`).

Good luck!

------------------------------------------------------------------------

# SmartX SignalR Hub (Plesk, Windows/IIS)

Bereit für Deployment unter **Plesk (Windows)** als ASP.NET Core App.
Enthält: - Gesicherten **SignalR Hub** unter `/signalrhub` (JWT Bearer,
HS256) - `web.config` für IIS/Plesk inkl. **WebSockets** - Testseite
unter `/index.html` - Test-Token-Endpoint `/token?user=Name` (nur für
Tests; in Produktion entfernen)

## 1) Voraussetzungen

-   **.NET 8 Hosting Bundle** auf dem Windows-Server installieren
    (danach Neustart).
-   In Plesk: SSL/TLS (Let's Encrypt) aktivieren.

## 2) Deployment (Plesk)

-   Neue Domain/Subdomain oder bestehende verwenden, z. B.
    signalr.deinedomain.tld.
-   Ordner (Dokumentstamm) z. B. `httpdocs\signalr`.
-   **Alle Dateien** aus diesem ZIP in den Ordner hochladen.
-   Nichts kompilieren auf dem Server -- es ist eine Framework-dependent
    App.

## 3) Geheimnis (HMAC Key) setzen

Am besten als **Umgebungsvariable** in Plesk/IIS: - Name:
`SMARTX_HMAC_B64` - Wert: 32 Byte zufällig, Base64-kodiert.

### Token generieren (PowerShell auf Windows):

``` powershell
$bytes = New-Object 'System.Byte[]' 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

Alternativ `appsettings.json` → `Auth:HmacKeyBase64` setzen (nicht
empfohlen im Repo).

## 4) WebSockets aktivieren

-   In Windows Server-Rollen: **WebSocket-Protokoll** aktivieren.
-   In `web.config` ist `<webSocket enabled="true" />` bereits gesetzt.

## 5) Start & Test

-   Browser: `https://deinedomain.tld/index.html`
-   Hole Testtoken: `https://deinedomain.tld/token?user=Daniel`
-   Token in die Seite einfügen → **Connect**.
-   Nachrichten mit **Echo**/**Broadcast** testen.

> Hinweis: `/token` ist nur zu Testzwecken gedacht. In Produktion bitte
> entfernen oder hinter Adminschutz legen.

## 6) Client-Konfiguration (C#)

``` csharp
var jwt = "DEIN_TOKEN"; // erhalten z. B. aus deinem Login-Flow
var connection = new HubConnectionBuilder()
    .WithUrl("https://deinedomain.tld/signalrhub", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(jwt);
    })
    .WithAutomaticReconnect()
    .Build();

await connection.StartAsync();
```

## 7) Häufige Fehler

-   **502.5 Process Failure** → .NET Hosting Bundle fehlt oder falsche
    Version.
-   **WebSocket schlägt fehl** → Rolle nicht installiert oder vom Hoster
    deaktiviert.
-   **IDX10653 Key too small** → HMAC-Key \< 16 Bytes. 32 Bytes
    verwenden.
-   **CORS Fehler** → In `Program.cs` CORS anpassen (Whitelist statt
    `AllowAny...`).

Viel Erfolg!
