# SmartX SignalR Hub

SmartXSignalR provides the realtime backbone for SmartXChain nodes. The hub
handles:

- **Secure node authentication** via JWT (HS256).
- **Availability broadcasts** so peers discover each other immediately.
- **WebRTC signalling** (SDP offers/answers and ICE candidates) for high-bandwidth
  blockchain sync channels.
- **Request/response relays** for future cross-node coordination features.

The implementation is optimised for long running node clusters and recovers
automatically from transient network failures.

---

## 1. Configure authentication

SmartXChain nodes expect the hub to accept tokens derived from the environment
variable `SIGNALR_PASSWORD`. Configure one of the following options **before**
starting the hub:

1. **Recommended (shared secret)**

   ```text
   SIGNALR_PASSWORD = <your strong passphrase>
   SIGNALR_JWT_ISSUER = smartXchain   # optional, defaults to smartXchain
   ```

   The server derives the HS256 key from `issuer:password` using SHA-256, which
   matches the client implementation.

2. **Explicit HMAC key**

   Provide the raw 32-byte signing key (Base64 encoded):

   ```text
   SMARTX_HMAC_B64 = <Base64-encoded 32 byte secret>
   ```

   or set `Auth:HmacKeyBase64` inside `appsettings.json` for local testing.

If none of the above are supplied, the hub generates a volatile key on startup.
Restarting the process will invalidate previously issued tokens.

---

## 2. Hub tuning options

The section `SignalRHub` in `appsettings.json` (or environment overrides) allows
fine-tuning without touching code:

| Setting                          | Default | Description                                                        |
| -------------------------------- | ------- | ------------------------------------------------------------------ |
| `OfferResponseTimeoutSeconds`    | `15`    | Seconds to wait for a peer to return a WebRTC SDP offer.          |
| `OfferCacheDurationSeconds`      | `30`    | Accept cached offers newer than this value to reduce churn.       |
| `MaxPendingOfferRequests`        | `2048`  | Upper bound for concurrent outstanding offer requests.            |

All values can be overridden via environment variables using the
`SignalRHub__SettingName` syntax (e.g. `SignalRHub__OfferResponseTimeoutSeconds`).

---

## 3. Running locally

```bash
cd SmartXSignalR
export SIGNALR_PASSWORD="super-secure"
dotnet run
```

During development a helper endpoint issues temporary tokens:
`GET /token?user=alice`. The endpoint is **disabled** automatically in
production environments.

A diagnostics client is available at `/index.html`. Paste a JWT token and use
"Broadcast" to watch node discovery messages in real time.

---

## 4. Deployment on Windows / IIS / Plesk

1. Install the **.NET 8 Hosting Bundle** on the server (reboot afterwards).
2. Deploy the published output (or the repo) to your site (e.g. `httpdocs\signalr`).
3. Ensure WebSockets are enabled (`web.config` already contains `<webSocket enabled="true" />`).
4. Configure one of the authentication secrets described above.
5. Start the application. The hub endpoint lives at `/signalrhub`.

### Troubleshooting

| Symptom                         | Cause & Fix                                                                |
|--------------------------------|----------------------------------------------------------------------------|
| `IDX10653 Key too small`       | The derived HMAC key is shorter than 32 bytes – provide a stronger secret. |
| WebSocket upgrade fails        | WebSocket role missing or blocked by hosting provider.                      |
| Clients reconnect repeatedly   | Check firewall / reverse proxy idle timeouts and enable forward headers.   |

---

## 5. Hinweise (Deutsch)

- **Authentifizierung:** Am einfachsten `SIGNALR_PASSWORD` setzen (siehe oben);
  alternativ `SMARTX_HMAC_B64` für einen festen HS256-Schlüssel verwenden.
- **Konfiguration:** Werte im Abschnitt `SignalRHub` steuern Timeout und Caching
  für SDP-Angebote.
- **Testclient:** `/index.html` liefert ein kleines Diagnose-Tool. Token kann
  lokal über `/token?user=<Name>` erzeugt werden (nur in Nicht-Produktiv-Umgebungen verfügbar).
- **Bereitstellung:** .NET 8 Hosting Bundle installieren, WebSockets aktivieren,
  Dateien in den Zielordner kopieren und Site starten.

Viel Erfolg beim Betrieb des SmartX Netzwerks!
