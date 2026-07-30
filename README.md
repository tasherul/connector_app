# Retwho Connector

Retwho Connector is a Windows desktop agent that connects a local HTTPS
NAXML POS endpoint to the fixed Socket.IO bridge at
`https://connector.retwho.com`. It authenticates locally, keeps the returned
POS cookie encrypted for the current Windows user, registers as a
`localhost_agent`, and answers `get_current_data` with fresh `vdatetime` data.

The connector never accesses bridge MySQL, registers licenses, sends POS
credentials or cookies to the cloud, or disables bridge certificate
validation.

## Requirements

- Windows 10 or Windows 11 x64.
- .NET 10 SDK.
- Visual Studio with the **.NET desktop development** workload, or the .NET
  CLI.
- Network access to the local POS HTTPS origin and
  `https://connector.retwho.com`.
- An active Retwho connector license.

## Build in Visual Studio

1. Open `RetwhoConnector.sln`.
2. Select the `RetwhoConnector.App` startup project.
3. Restore NuGet packages.
4. Select `Release` and `Any CPU` or `x64`.
5. Build and run.

CLI equivalents:

```powershell
dotnet restore
dotnet build RetwhoConnector.sln -c Release
dotnet test RetwhoConnector.sln -c Release --no-build
```

## First-run setup

1. Enter the Retwho license key.
2. Enter only the local POS HTTPS origin, for example
   `https://10.1.10.250`. Paths, query strings, fragments, HTTP URLs, and URLs
   containing user information are rejected.
3. Enter the POS username and password.
4. If the POS certificate is self-signed, select **Trust POS Certificate**,
   verify the displayed SHA-256 fingerprint with the POS administrator, and
   approve it.
5. Select **Test POS Login**.
6. Select **Save & Connect**.

The bridge URL is fixed and is intentionally not editable.

## POS login and cookie handling

The connector sends the legacy `validate` NAXML command using the required
query, body, and header profile. It parses the bounded XML response with DTDs
disabled and extracts the application cookie from the XML `<cookie>` element.
It does not use HTTP `Set-Cookie` as the NAXML application cookie.

The username, password, license, POS cookie, and approved certificate
fingerprint are encrypted with Windows DPAPI using `CurrentUser` scope.
Settings are stored at:

```text
%LocalAppData%\RetwhoConnector\settings.json
```

Changing the POS origin clears the cached cookie and certificate pin.
Changing POS credentials clears the cached cookie.

## Self-signed POS certificates

Certificate discovery opens a credential-free TLS connection. It displays
only the subject, issuer, validity dates, policy state, and SHA-256
fingerprint. Normal Windows trust is accepted without a pin. A self-signed
certificate is accepted only when:

- the user explicitly approved its fingerprint;
- the request uses the same configured HTTPS scheme, host, and port; and
- the presented SHA-256 fingerprint matches exactly.

A changed certificate is rejected until the new fingerprint is independently
verified and approved. This POS-only callback is never used for the cloud
bridge.

## Bridge connection and registration

Socket.IO uses Engine.IO v4, WebSocket transport, path `/socket.io`, strict
system TLS, and handshake authentication:

```json
{ "licenseKey": "EXAMPLE-LICENSE-001" }
```

After every connection or reconnection the connector emits:

```json
{
  "licenseKey": "EXAMPLE-LICENSE-001",
  "clientType": "localhost_agent"
}
```

Transport **Connected** is not the same as agent **Registered**. Commands are
accepted only after an acknowledgement with `ok: true` and
`code: "REGISTERED"`. Invalid or inactive licenses stop reconnecting until
settings change. `session_replaced` stops the older connector until manual
reconnection.

## `get_current_data` flow

1. The bridge sends `execute_local_action`.
2. The connector validates the action and enters the duplicate-action
   registry.
3. It starts one shared eight-second deadline.
4. It sends a fresh POS `vdatetime` request.
5. If the cookie is expired, it performs one `validate`, encrypts the new
   cookie, and retries `vdatetime` once.
6. It parses XML, maps it to camelCase JSON, checks the one-MiB bridge limit,
   and acknowledges exactly once.

Connecting or registering never calls `vdatetime`. The connector does not
poll and does not also send `agent_data_push` for this command.

## Required NAXML request profile

Both `validate` and `vdatetime` use this profile:

| Property/header | Value |
|---|---|
| Method | `POST` |
| Query and body | Identical encoded command values |
| `Content-Type` | `text/plain; charset=UTF-8` |
| `Content-Length` | UTF-8 byte count |
| HTTP version | HTTP/1.1, request version or lower |
| `User-Agent` | `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36` |
| `Referer` | `{POS origin}/ConfigClient.html` |
| `Host` | Validated POS authority |
| Connection | `keep-alive` |
| `Accept-Encoding` | `gzip, deflate, br, zstd` |
| `Accept-Language` | `en-US,en;q=0.9,bn;q=0.8` |
| `Origin` | POS origin |
| `Sec-Fetch-Dest` | `empty` |
| `Sec-Fetch-Mode` | `cors` |
| `Sec-Fetch-Site` | `same-origin` |
| `sec-ch-ua` | `"Not;A=Brand";v="8", "Chromium";v="150", "Google Chrome";v="150"` |
| `sec-ch-ua-mobile` | `?0` |
| `sec-ch-ua-platform` | `"Windows"` |

No `Accept` header is invented. `HttpClient` sets request headers on
`request.Headers`; `Content-Type` and `Content-Length` are set on
`request.Content.Headers`.

## Response headers and compression

Headers are captured on both success and error responses before status
classification. Only this safe allowlist can be logged:

- status code;
- content type and declared length;
- content encodings;
- date and server;
- retry delay;
- Boolean presence of `Set-Cookie` and `WWW-Authenticate`.

Header strings have control characters removed and are length-limited.
Values of `Set-Cookie`, `WWW-Authenticate`, authorization headers, and unknown
headers are never logged.

`Content-Length` is checked before reading. The streamed, decompressed body is
also bounded to two MiB. gzip, deflate, Brotli, and zstd are decoded in
reverse application order. Unknown encodings return
`POS_UNSUPPORTED_CONTENT_ENCODING`.

## Safe XML and JSON examples

Fake login response:

```xml
<?xml version="1.0"?>
<credential>
  <cookie>FAKE_COOKIE_FOR_TESTS_ONLY</cookie>
  <site>6720</site>
</credential>
```

Command result:

```json
{
  "source": "NAXML",
  "command": "vdatetime",
  "siteId": "6720",
  "systemDateTime": "2026-07-30T14:31:18-04:00",
  "systemTimeZoneId": "US/Eastern",
  "timeZones": [
    {
      "timeZoneId": "US/Eastern",
      "offsetMinutes": -300,
      "dstApplies": true
    }
  ],
  "rawXml": "<sysDateTime>...</sysDateTime>",
  "fetchedAtUtc": "2026-07-31T08:20:01.250Z"
}
```

XML namespaces are matched by local name. DTDs and external entities are
prohibited. Offsets must be integers and `dstApplies` must be `0` or `1`.

## Logs and local activity

Rolling logs are written to:

```text
%LocalAppData%\RetwhoConnector\Logs\
```

Each file is limited to 10 MiB and ten files are retained. Logs contain safe
operation names, states, durations, status codes, and redacted error details.
They never contain POS passwords, cookies, full licenses, login request
URIs/bodies, raw login XML, settings objects, or sensitive header values.
The UI keeps the newest 200 safe activity messages.

## Error reference

| Code | Meaning |
|---|---|
| `INVALID_ACTION` | The bridge action is malformed |
| `UNSUPPORTED_COMMAND` | v1 supports only `get_current_data` |
| `POS_LOGIN_FAILED` | POS credentials were rejected or login XML was invalid |
| `POS_AUTH_EXPIRED` | The cached POS session is no longer valid |
| `POS_CERTIFICATE_UNTRUSTED` | A POS certificate needs approval |
| `POS_CERTIFICATE_CHANGED` | The approved fingerprint no longer matches |
| `POS_TIMEOUT` | The shared command deadline expired |
| `POS_HTTP_ERROR` | The POS returned another unsuccessful HTTP status |
| `POS_UNSUPPORTED_CONTENT_ENCODING` | The response used an unknown encoding |
| `POS_INVALID_XML` | XML was malformed or unsafe |
| `POS_INVALID_RESPONSE` | Valid input did not match the expected contract |
| `PAYLOAD_TOO_LARGE` | The command result exceeded one MiB |
| `COMMAND_CANCELLED` | Shutdown or session cancellation stopped the command |
| `INTERNAL_ERROR` | A safe unexpected/busy failure |

## Clear encrypted settings

Select **Clear Saved Settings** and confirm. The connector disconnects,
deletes only its known settings file, clears the form, and returns to the
unconfigured state. A corrupt settings file is not silently overwritten;
back it up from the path above before clearing it.

## Run tests

```powershell
dotnet test RetwhoConnector.sln -c Release
```

Tests use fake handlers, fake Socket.IO adapters, fake credentials, and local
XML fixtures. They do not contact the real POS or production bridge.

## Publish Windows x64

```powershell
dotnet publish src/RetwhoConnector.App/RetwhoConnector.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Output:

```text
src\RetwhoConnector.App\bin\Release\net10.0-windows\win-x64\publish\
```

Trimming is disabled because WPF and Socket.IO trim safety has not been
proven.

## Troubleshooting

- **Certificate approval required:** compare the SHA-256 fingerprint with the
  POS administrator; do not approve an unknown certificate.
- **Certificate changed:** verify whether the POS certificate was
  intentionally replaced before approving it.
- **Login failure:** verify the HTTPS origin and POS credentials. Never paste
  credentials into logs or issue reports.
- **Unsupported encoding:** capture only safe response metadata and ask the
  POS administrator which `Content-Encoding` was sent.
- **Connected but not registered:** check the license state and bridge
  registration code.
- **Timeout:** verify local routing/firewall latency; the complete command,
  including one session refresh, has an eight-second internal deadline.
- **Repeated replacement:** stop other connectors using the same license.

## English quick start

Enter the active license, local POS HTTPS origin, username, and password.
Verify and trust a self-signed POS fingerprint, test POS login, then select
**Save & Connect**. Wait until both Bridge Transport shows **Connected** and
Agent Registration shows **Registered**.

## বাংলা দ্রুত শুরু

সক্রিয় লাইসেন্স কী, লোকাল POS-এর HTTPS ঠিকানা, ইউজারনেম এবং পাসওয়ার্ড
লিখুন। POS সার্টিফিকেট self-signed হলে POS অ্যাডমিনের সাথে SHA-256
fingerprint মিলিয়ে **Trust POS Certificate** চাপুন। তারপর **Test POS
Login** এবং **Save & Connect** চাপুন। Bridge Transport-এ **Connected** এবং
Agent Registration-এ **Registered**—দুইটি অবস্থা দেখা গেলে সংযোগ প্রস্তুত।
পাসওয়ার্ড, cookie বা পূর্ণ license key কখনও লগ বা সহায়তা বার্তায় দেবেন
না।
