# Hybrid Edge Connector Agent

Hybrid Edge Connector Agent is the visible name of the Retwho Connector
Windows desktop application. It connects a local HTTPS NAXML POS endpoint to
the fixed Socket.IO bridge at
`https://connector.retwho.com`. It authenticates locally, keeps the returned
POS cookie encrypted for the current Windows user, registers as a
`localhost_agent`, and answers `get_current_data` with fresh `vdatetime` data.

The executable, solution, namespaces, storage folder, and protocol identity
retain their technical `RetwhoConnector` names for compatibility. The agent
never accesses bridge MySQL, registers licenses, sends POS
credentials or cookies to the cloud, or disables bridge certificate
validation.

## Architecture

- `RetwhoConnector.App` is the WPF dashboard, configuration dialog,
  notification-area integration, host startup, and dependency wiring.
- `RetwhoConnector.Core` owns DPAPI settings, POS TLS and NAXML transport,
  bounded XML/response processing, the Socket.IO bridge, command
  coordination, sanitization, and the logging pipeline.
- `RetwhoConnector.Tests` contains isolated fake-credential tests. It never
  contacts the production POS or cloud.

`ConnectorCoordinator` remains the sole owner of accepted bridge-command
execution, deadlines, duplicate sharing, cookie refresh, and exactly-once
acknowledgements. `AgentOrchestrationService` is the UI-facing lifecycle
façade.

## Requirements

- Windows 10 or Windows 11 x64.
- Visual Studio 2026 (version 18.0 or newer) with the **.NET desktop
  development** workload. Visual Studio 2022 does not officially support
  projects targeting `net10.0`.
- For command-line builds without Visual Studio, the .NET 10 SDK.
- Network access to the local POS HTTPS origin and
  `https://connector.retwho.com`.
- An active Retwho connector license.

## Build in Visual Studio

1. On Windows, double-click `RetwhoConnector.sln`.
2. If Visual Studio reports missing components, accept the prompt generated
   from `.vsconfig` to install the **.NET desktop development** workload.
3. In the launch-profile list, select **Hybrid Edge Connector Agent**. The checked-in
   `RetwhoConnector.slnLaunch` profile starts only
   `RetwhoConnector.App`.
4. Leave **Debug** and **Any CPU** selected for normal development.
5. Press **F5** to build and run with the debugger, or **Ctrl+F5** to run
   without it.

The solution contains the WPF app, its Core library, and the test project.
NuGet restore normally starts automatically when the solution opens. If it
does not, right-click the solution and select **Restore NuGet Packages**.

CLI equivalents:

```powershell
dotnet restore
dotnet build RetwhoConnector.sln -c Release
dotnet test RetwhoConnector.sln -c Release --no-build
```

## First-run setup

All configuration fields are empty on first run.

1. Start the application and select **Settings**.
2. Enter the Retwho license key.
3. In **HTTPS origin**, enter only the local POS URL, for example
   `https://10.1.10.250`. Paths, query strings, fragments, HTTP URLs, and URLs
   containing user information are rejected.
4. Enter the POS username and password.
5. Select **Save & Test Connection**.
6. If the credential-free TLS inspection finds a certificate that Windows
   does not trust, independently verify the displayed SHA-256 fingerprint
   with the POS administrator, then approve it.

The save workflow validates the draft, inspects certificate trust, tests POS
login, encrypts the settings and returned cookie atomically, and then
connects/registers the bridge. If validation, certificate approval, or POS
login fails, the old saved settings remain unchanged. If POS login and
encrypted save succeed but the cloud is unavailable, the valid saved POS
configuration remains available for a later **Connect**.

The bridge URL is fixed and is intentionally not editable.

## Status dashboard

The dashboard has four accessible status cards:

| Card | Typical meanings |
|---|---|
| Config | **Configured**, **Missing configuration**, or **Invalid** |
| `connector.retwho.com` | **Connected**, **Connecting**, **Reconnecting**, **Offline**, **Authentication failed**, or **Session replaced** |
| Agent | **Active** only when registered; otherwise **Idle**, **Inactive**, or **Error** |
| Logs | **Healthy**, **Degraded** with a dropped-entry count, or **Stopped** |

Green indicates success, yellow indicates session/warning or transitional
states, blue activity rows identify actions/acknowledgements, red indicates
errors/timeouts/authentication failures, and neutral rows are informational.
The terminal timestamps are converted from stored UTC to local `HH:mm:ss`.

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
5. If HTTP 401/403, the existing bounded expiry heuristic, or the exact
   Sapphire fault `CGIPortal.LoginRequired` reports an expired session, it
   performs one `validate` login and one `vdatetime` retry. The replacement
   XML cookie is encrypted before the retry result is acknowledged.
6. It parses XML, maps it to camelCase JSON, checks the one-MiB bridge limit,
   and acknowledges exactly once.

Connecting or registering never calls `vdatetime`. The connector does not
poll and does not also send `agent_data_push` for this command.

## PLU and referential bridge actions

The bridge can also send `get_plu_page`, `get_plu`, and
`get_referential_integrity` through `execute_local_action`. Each action uses
the existing bounded action ID, object-valued `params`, eight-second command
deadline, one-MiB acknowledged JSON limit, duplicate sharing, and exactly-once
acknowledgement rules. Invalid parameters are rejected as `INVALID_ACTION`
before the connector loads POS credentials or contacts the POS.

### `get_plu_page`

`get_plu_page` reads a bounded PLU page. `page` defaults to `1` and must be a
positive integer. `pageSize` defaults to `100` and must be an integer from
`1` through `100`. One bridge action retrieves exactly one POS page; the
cloud owns pagination and requests any next page using `totalPages` from the
previous acknowledgement. The connector never fetches every page on the
cloud's behalf.

Synthetic request:

```json
{
  "actionId": "FAKE-PLU-PAGE-001",
  "command": "get_plu_page",
  "params": {
    "page": 2,
    "pageSize": 25
  }
}
```

Synthetic successful result:

```json
{
  "ok": true,
  "result": {
    "source": "NAXML",
    "command": "vPLUs",
    "page": 2,
    "totalPages": 4,
    "requestedPageSize": 25,
    "itemCount": 1,
    "products": [
      {
        "upc": "00000000000001",
        "upcModifier": "000",
        "description": "FAKE PRODUCT A",
        "departmentId": "10",
        "feeIds": ["0"],
        "price": 4.67,
        "flagIds": [],
        "taxRateIds": ["2"],
        "idCheckIds": [],
        "groupCodes": []
      }
    ],
    "fetchedAtUtc": "2026-07-31T00:00:00Z"
  }
}
```

### `get_plu`

`get_plu` reads one exact PLU. `upc` is required and must contain one through
32 decimal digits; its leading zeroes are preserved. `upcModifier` defaults to `000`
and must contain exactly three decimal digits. The connector uses a
fixed POS selector page of `1` and page size of `100` for this exact lookup.

Synthetic request with the default modifier:

```json
{
  "actionId": "FAKE-PLU-LOOKUP-001",
  "command": "get_plu",
  "params": {
    "upc": "00000000000002"
  }
}
```

An empty valid POS result is a successful lookup, not an error: the bridge
acknowledgement has `ok: true` and `found: false`, and it omits `product`.

```json
{
  "ok": true,
  "result": {
    "source": "NAXML",
    "command": "vPLU",
    "requestedUpc": "00000000000002",
    "requestedUpcModifier": "000",
    "found": false,
    "fetchedAtUtc": "2026-07-31T00:00:00Z"
  }
}
```

### `get_referential_integrity`

`get_referential_integrity` accepts only an empty `params` object. It always
requests the fixed dataset list
`prodCodes,departments,ageValidations,taxRates,blueLaws,fees` in that order.
The cloud cannot add, remove, reorder, or inject dataset names.

Synthetic request and abbreviated result:

```json
{
  "actionId": "FAKE-REFERENTIAL-001",
  "command": "get_referential_integrity",
  "params": {}
}
```

```json
{
  "ok": true,
  "result": {
    "source": "NAXML",
    "command": "vrefinteg",
    "siteId": "FAKE-SITE",
    "limits": {
      "maxRecords": 100,
      "maxFeesPerItem": 8
    },
    "taxRates": [],
    "departments": [],
    "productCodes": [],
    "ageValidations": [],
    "fees": [],
    "blueLaws": [],
    "fetchedAtUtc": "2026-07-31T00:00:00Z"
  }
}
```

All three new results use camelCase JSON, preserve identifiers as strings,
omit null optional fields, and return collection properties as empty arrays.
They omit `rawXml`, POS origins, cookies, request details, and diagnostic
metadata.

### Shared POS session recovery

Every POS data action first tries the saved cookie once. If the POS identifies
an expired session through HTTP or the supported XML fault, the connector
performs one `validate` login and one retry, atomically saving the replacement
encrypted cookie before retrying the original action. A second authentication
failure returns `POS_AUTH_EXPIRED`; it does not cause another login or retry.

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

Every entry is sanitized before it enters the bounded channel, so raw secret
data cannot reach a sink. The terminal keeps exactly the newest 1,000 safe
entries. File logs are written to:

```text
%LocalAppData%\RetwhoConnector\Logs\
```

Daily `agent-YYYY-MM-DD.log` files roll at 10 MiB and files older than 14 days
are deleted. Sanitized historical entries are also stored in SQLite at:

```text
%LocalAppData%\RetwhoConnector\Data\agent.db
```

SQLite uses WAL mode, batches inserts, keeps 30 days, and trims to at most
100,000 rows. It stores logs only; encrypted configuration remains exclusively
in `settings.json`. Logs contain safe operation names, states, durations,
status codes, and redacted error details. They never contain POS usernames or
passwords, cookies, full licenses, credential-bearing request URIs/bodies,
raw XML, settings objects, ciphertext, authorization values, or sensitive
header values.

PLU products and referential records are inventory payloads. Logs never
contain inventory payloads or credential-bearing requests, including their
query strings and bodies.

The safe POS response-header metadata is status code, content type, declared
length, content encodings, date, server, retry delay, and Boolean presence of
`Set-Cookie` or `WWW-Authenticate`. Status code controls HTTP
classification; declared length is checked before reading; content encodings
control bounded reverse-order decompression; and retry metadata is retained
only as a safe bounded value. Cookie-expiry retry is driven by HTTP/XML
session classification, not by an unsafe header value.

For every POS operation, diagnostics record only the command, method, HTTP
version, declared byte length, certificate-pin presence, duration, safe
response metadata, response character count, XML root name, and the
allowlisted fault fields `faultCode`, `faultString`, and `message`. The agent
does not log raw request or response payloads. In particular, it never writes
the credential-bearing request URI/body, cookie, password, username, full
license, unrestricted XML, or an exception stack trace. Microsoft
`HttpClientFactory` packet/stack logging is disabled for the POS client so
these bounded records are the only HTTP diagnostics routed to the sinks.

## Notification-area behavior

Minimizing the main window or selecting its close button hides it while POS,
cloud, and logging work continue. Use the notification-area icon to:

- double-click or select **Show** to restore the dashboard;
- open **Settings**;
- **Connect** or **Disconnect**; or
- select **Exit** for a real shutdown.

Only **Exit** stops command acceptance, disconnects Socket.IO, drains queued
logs for up to five seconds, removes the tray icon, and shuts down the host.
The secret-free marker at
`%LocalAppData%\RetwhoConnector\agent.running` is removed only after a clean
shutdown. If it remains after a crash or forced termination, the next launch
adds a safe unclean-exit warning to the activity log.

## Error reference

| Code | Meaning |
|---|---|
| `INVALID_ACTION` | The bridge action is malformed |
| `UNSUPPORTED_COMMAND` | The requested bridge command is not supported |
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

Open **Settings**, select **Clear Saved Settings**, and confirm. The connector
disconnects, deletes only its known settings file, clears the dialog, and
returns to the unconfigured state. A corrupt settings file is not silently
overwritten; back it up from the path above before clearing it.

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

- **`Microsoft.Extensions.Configuration.FileExtensions` is missing:** update
  to the latest `main`, select **Build > Clean Solution**, close Visual
  Studio, delete only the generated `bin` and `obj` folders, reopen the
  solution, restore NuGet packages, and select **Rebuild Solution**. Do not
  copy the small executable from a normal `bin` folder by itself; run it with
  all adjacent DLLs or use the self-contained executable from the `publish`
  folder.
- **Certificate approval required:** compare the SHA-256 fingerprint with the
  POS administrator; do not approve an unknown certificate.
- **Certificate changed:** verify whether the POS certificate was
  intentionally replaced before approving it. When
  `POS_CERTIFICATE_CHANGED` appears, reopen **Settings**, run
  **Save & Test Connection**, independently compare the new SHA-256
  fingerprint, and approve it only if expected.
- **Login failure:** verify the HTTPS origin and POS credentials. Never paste
  credentials into logs or issue reports.
- **Unsupported encoding:** capture only safe response metadata and ask the
  POS administrator which `Content-Encoding` was sent.
- **Connected but not registered:** check the license state and bridge
  registration code.
- **Timeout:** verify local routing/firewall latency; the complete command,
  including one session refresh, has an eight-second internal deadline.
- **Repeated replacement:** stop other connectors using the same license.
- **Window disappeared after close/minimize:** the agent is still running.
  Double-click its notification-area icon. Select **Exit** there when a real
  shutdown is required.
- **Logging card is degraded:** open the logs folder and check disk space and
  permissions for the `Logs` and `Data` folders. The dropped count never
  contains payload data.
- **Previous shutdown warning:** Windows or the process ended before the host
  drained its workers. A normal notification-area **Exit** clears the safe
  startup marker.
- **`CGIPortal.LoginRequired`:** the saved XML cookie expired. The agent
  performs one login, saves the encrypted replacement, and retries
  `vdatetime` once. If the replacement is also rejected, it reports
  `POS_AUTH_EXPIRED` without another loop.

## English quick start

Select **Settings**, enter the active license, local POS HTTPS origin,
username, and password, then select **Save & Test Connection**. If prompted,
verify the self-signed POS fingerprint with the administrator before
approval. Wait until the cloud card shows **Connected** and the agent card
shows **Active**. Closing or minimizing the window keeps the agent running in
the notification area; use **Exit** there to stop it.

## বাংলা দ্রুত শুরু

সক্রিয় লাইসেন্স কী, লোকাল POS-এর HTTPS ঠিকানা, ইউজারনেম এবং পাসওয়ার্ড
লিখতে প্রথমে **Settings** চাপুন। তারপর **Save & Test Connection** চাপুন।
POS সার্টিফিকেট self-signed হলে POS অ্যাডমিনের সাথে SHA-256 fingerprint
মিলিয়ে অনুমোদন দিন। Cloud status-এ **Connected** এবং Agent status-এ
**Active**—দুইটি অবস্থা দেখা গেলে সংযোগ প্রস্তুত। উইন্ডো minimize বা close
করলে agent notification area-তে চলতে থাকবে; সম্পূর্ণ বন্ধ করতে tray menu
থেকে **Exit** চাপুন।
পাসওয়ার্ড, cookie বা পূর্ণ license key কখনও লগ বা সহায়তা বার্তায় দেবেন
না।
