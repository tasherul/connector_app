# Web-App Socket Integration Guide

This guide is for the trusted Node.js/TypeScript backend or cloud bridge that
routes commands to a registered Hybrid Edge Connector Agent. A browser must
never connect directly to the local POS or receive its credentials or cookie.

All values and payloads below are synthetic. This guide describes the current
connector contract serialized with camelCase JSON and null omission.

## Architecture and ownership

```text
Browser -> Web backend/cloud bridge -> execute_local_action
        -> Windows connector -> HTTPS/NAXML POS
        -> Socket.IO acknowledgement -> Web backend -> browser-safe response
```

The browser calls an application backend. That backend authorizes the user,
finds a registered connector, sends the action over the connector socket, and
transforms the acknowledgement into a browser-safe response. A browser must never call
the POS boundary. The Windows connector alone builds POS requests, owns the
encrypted POS cookie, and talks to the local HTTPS/NAXML POS.

> **Existing contract versus example application routes.** The fixed Socket.IO
> agent contract below is implemented. The cloud bridge must already have a
> registered-agent registry. A route such as
> `POST /api/connectors/:connectorId/actions` is an example route that a reader
> may implement; it is **not a deployed Retwho endpoint**. The agent license
> handshake establishes connector identity only and must not be reused as
> browser authentication.

## Fixed Socket.IO boundary

The connector connects by WebSocket (Engine.IO v4, no auto-upgrade) to the
fixed bridge URL `https://connector.retwho.com` with the Socket.IO path
`/socket.io`. On connection it emits `register_client` with its license in
socket authentication and this registration payload:

```json
{
  "licenseKey": "FAKE-LICENSE-IDENTITY",
  "clientType": "localhost_agent"
}
```

The bridge accepts the agent only after a successful registration response
whose client type is `localhost_agent`. The backend should look up a live,
registered connector using an internal connector/license identity; it must not
put a full license value in browser URLs, logs, analytics, or client-side
state. `session_replaced` is terminal for the older agent until manual
reconnection.

### Action envelope and acknowledgement

The bridge sends each command as `execute_local_action` with this exact shape:

```json
{
  "actionId": "FAKE-ACTION-001",
  "command": "get_current_data",
  "params": {},
  "timestamp": "2026-08-01T00:00:00Z"
}
```

`actionId` is the 1-128 character idempotency key. `params` must be a JSON
object, and `timestamp` is required. The connector shares duplicate action IDs
as one execution and delivers one acknowledgement exactly once. It applies an
eight-second connector deadline; the cloud Socket.IO acknowledgement boundary
is 10 seconds. A successful acknowledgement, serialized as JSON, must remain
strictly below one MiB (1,048,576 bytes).

Success and failure shapes are respectively:

```json
{ "ok": true, "result": {} }
```

```json
{ "ok": false, "error": "ERROR_CODE: Safe description." }
```

The result or error property not used is omitted. Treat a returned
acknowledgement as the delivery result for the action ID. If the delivery
outcome is unknown, retry the same logical operation with the same action ID;
do not turn it into a second POS operation by minting an ID prematurely.

## Server-side TypeScript dispatch

Use a trusted socket from the bridge registry, never a socket ID supplied by a
browser. The following is framework-light server-side code; adapt the import
and socket typing to the installed `socket.io` version. Socket.IO's timeout
acknowledgement form is documented by the official
[Socket.IO emitting-events documentation](https://socket.io/docs/v4/emitting-events/#with-timeout).

```ts
import type { Socket } from "socket.io";

type RegisteredAgentSocket = Socket;

type AgentAction = {
  actionId: string;
  command:
    | "get_current_data"
    | "get_plu_page"
    | "get_plu"
    | "get_referential_integrity";
  params: Record<string, unknown>;
  timestamp: string;
};

type AgentAck<T> =
  | { ok: true; result: T }
  | { ok: false; error: string };

async function executeAgentAction<T>(
  socket: RegisteredAgentSocket,
  action: AgentAction,
): Promise<AgentAck<T>> {
  return await new Promise((resolve, reject) => {
    socket.timeout(10_000).emit(
      "execute_local_action",
      action,
      (timeoutError: Error | null, acknowledgement?: AgentAck<T>) => {
        if (timeoutError) {
          reject(new Error("Connector acknowledgement timed out."));
          return;
        }

        if (!acknowledgement || typeof acknowledgement.ok !== "boolean") {
          reject(new Error("Connector acknowledgement was invalid."));
          return;
        }

        resolve(acknowledgement);
      },
    );
  });
}
```

`RegisteredAgentSocket` means the trusted socket returned by the cloud
bridge's registered-agent lookup. It is not a browser-provided socket and its
lookup key is an internal connector/license identity.

## Result types shared with the backend

The connector serializes its C# result contracts using `ConnectorJson.Options`:
camelCase names, case-insensitive reads, and omission of null fields. Preserve
identifiers as strings. A web backend should allow-list fields for browser
responses. In particular, `VdatetimeResult` currently includes `rawXml`; do
not forward `rawXml`, POS origins, cookies, request details, or diagnostics to
the browser.

```ts
type TimeZoneInfo = {
  timeZoneId: string;
  offsetMinutes: number;
  dstApplies: boolean;
};

type CurrentDataResult = {
  source: "NAXML";
  command: "vdatetime";
  siteId: string;
  systemDateTime: string;
  systemTimeZoneId: string;
  timeZones: TimeZoneInfo[];
  rawXml: string;
  fetchedAtUtc: string;
};

type IndexedCode = { index: number; code: string };
type PluProduct = {
  upc: string;
  upcModifier: string;
  description: string;
  departmentId: string;
  feeIds: string[];
  productCode?: string;
  price?: number;
  flagIds: string[];
  taxRateIds: string[];
  idCheckIds: string[];
  sellUnit?: number;
  taxableRebateAmount?: number;
  groupCodes: IndexedCode[];
  maxQuantityPerTransaction?: number;
};

type PluPageResult = {
  source: "NAXML";
  command: "vPLUs";
  page: number;
  totalPages: number;
  requestedPageSize: number;
  itemCount: number;
  products: PluProduct[];
  fetchedAtUtc: string;
};

type PluLookupResult = {
  source: "NAXML";
  command: "vPLU";
  requestedUpc: string;
  requestedUpcModifier: string;
  found: boolean;
  product?: PluProduct;
  fetchedAtUtc: string;
};

type DatasetLimit = { maxRecords: number; maxFeesPerItem?: number };
type ReferenceDefinition = {
  recordType: string;
  id?: string;
  name?: string;
  fields: Record<string, string>;
};
type ReferentialIntegrityResult = {
  source: "NAXML";
  command: "vrefinteg";
  siteId: string;
  limits: {
    taxRates?: DatasetLimit;
    departments?: DatasetLimit;
    prodCodes?: DatasetLimit;
    ageValidations?: DatasetLimit;
    blueLaws?: DatasetLimit;
    fees?: DatasetLimit;
  };
  taxRates: Array<{ id: string; name: string }>;
  departments: Array<{
    id: string; name: string; isFuel: boolean; productCode?: string;
  }>;
  productCodes: Array<{ id: string; name: string; isFuel: boolean }>;
  ageValidations: Array<{ id: string; name: string }>;
  fees: ReferenceDefinition[];
  blueLaws: ReferenceDefinition[];
  fetchedAtUtc: string;
};
```

## Commands and connector-to-POS boundaries

The URLs in this section are structural, redacted references only. Every one
is **connector-to-POS only—not browser-callable**. Query/form credentials and
cookies are built by the Windows connector, not TypeScript.

### `get_current_data`

Use `{}` for this command. The current connector requires `params` to be an
object but does not read properties for `get_current_data`; callers should not
rely on ignored properties and should send an empty object.

```json
{
  "actionId": "FAKE-CURRENT-DATA-001",
  "command": "get_current_data",
  "params": {},
  "timestamp": "2026-08-01T00:00:00Z"
}
```

It returns `AgentAck<CurrentDataResult>`. An abbreviated successful
acknowledgement is:

```json
{
  "ok": true,
  "result": {
    "source": "NAXML",
    "command": "vdatetime",
    "siteId": "FAKE-SITE",
    "systemDateTime": "2026-08-01T00:00:00",
    "systemTimeZoneId": "UTC",
    "timeZones": [],
    "rawXml": "<synthetic />",
    "fetchedAtUtc": "2026-08-01T00:00:00Z"
  }
}
```

Connector request reference — **connector-to-POS only—not browser-callable**:

```text
POST https://POS_HOST/cgi-bin/NAXML?cmd=vdatetime&cookie=FAKE_COOKIE
```

### `get_plu_page`

`page` defaults to `1` and must be an integer from `1` through `2147483647`.
`pageSize` defaults to `100` and must be an integer from `1` through `100`.
Each action retrieves exactly one POS page.

```json
{
  "actionId": "FAKE-PLU-PAGE-002",
  "command": "get_plu_page",
  "params": { "page": 2, "pageSize": 25 },
  "timestamp": "2026-08-01T00:00:00Z"
}
```

It returns `AgentAck<PluPageResult>`.

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
    "products": [{ "upc": "00000000000001", "upcModifier": "000", "description": "FAKE PRODUCT", "departmentId": "FAKE-DEPT", "feeIds": [], "flagIds": [], "taxRateIds": [], "idCheckIds": [], "groupCodes": [] }],
    "fetchedAtUtc": "2026-08-01T00:00:00Z"
  }
}
```

Connector request reference — **connector-to-POS only—not browser-callable**:

```text
POST https://POS_HOST/cgi-bin/NAXML?cmd=vPLUs&cookie=FAKE_COOKIE
```

The connector sends this `PLUSelect` body for the page (synthetic values):

```xml
<domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01"><pageSize>25</pageSize><page>2</page></domain:PLUSelect>
```

### `get_plu`

`upc` is required and must contain 1-32 decimal digits, preserving leading
zeroes. `upcModifier` defaults to `000` and must contain exactly three decimal
digits. No other parameter properties are accepted.

```json
{
  "actionId": "FAKE-PLU-LOOKUP-001",
  "command": "get_plu",
  "params": { "upc": "00000000000002" },
  "timestamp": "2026-08-01T00:00:00Z"
}
```

It returns `AgentAck<PluLookupResult>`. An exact lookup that finds no product
is successful (`ok: true`, `found: false`) and omits `product`.

```json
{
  "ok": true,
  "result": {
    "source": "NAXML",
    "command": "vPLU",
    "requestedUpc": "00000000000002",
    "requestedUpcModifier": "000",
    "found": false,
    "fetchedAtUtc": "2026-08-01T00:00:00Z"
  }
}
```

Connector request reference — **connector-to-POS only—not browser-callable**:

```text
POST https://POS_HOST/cgi-bin/NAXML?cmd=vPLUs&cookie=FAKE_COOKIE
```

The connector sends this exact-lookup `PLUSelect` body (synthetic UPC only):

```xml
<domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01"><query><where><upc source="keyboard">00000000000002</upc><upcModifier>000</upcModifier></where></query><pageSize>100</pageSize><page>1</page></domain:PLUSelect>
```

### `get_referential_integrity`

Parameters must be `{}`; no parameter properties are accepted.

```json
{
  "actionId": "FAKE-REFERENTIAL-001",
  "command": "get_referential_integrity",
  "params": {},
  "timestamp": "2026-08-01T00:00:00Z"
}
```

It returns `AgentAck<ReferentialIntegrityResult>`. A successful response has
all six dataset-limit entries and arrays for tax rates, departments, product
codes, age validations, fees, and blue laws:

```json
{
  "ok": true,
  "result": {
    "source": "NAXML",
    "command": "vrefinteg",
    "siteId": "FAKE-SITE",
    "limits": {
      "taxRates": { "maxRecords": 10 },
      "departments": { "maxRecords": 20 },
      "prodCodes": { "maxRecords": 30 },
      "ageValidations": { "maxRecords": 8 },
      "blueLaws": { "maxRecords": 0 },
      "fees": { "maxRecords": 25, "maxFeesPerItem": 3 }
    },
    "taxRates": [],
    "departments": [],
    "productCodes": [],
    "ageValidations": [],
    "fees": [],
    "blueLaws": [],
    "fetchedAtUtc": "2026-08-01T00:00:00Z"
  }
}
```

Connector request reference — **connector-to-POS only—not browser-callable**:

```text
POST https://POS_HOST/cgi-bin/NAXML?cmd=vrefinteg&dataset=prodCodes,departments,ageValidations,taxRates,blueLaws,fees&cookie=FAKE_COOKIE
```

The connector's recovery login is also **connector-to-POS only—not
browser-callable**:

```text
POST https://POS_HOST/cgi-bin/NAXML?cmd=validate&user=FAKE_USER&passwd=REDACTED
```

## Backend-owned PLU pagination

The backend, not the browser or connector, owns pagination. One bridge action
retrieves exactly one POS page, so create a distinct action ID per page. If a
page's delivery outcome is unknown, retry that same logical page with its
original action ID.

```ts
async function getAllPluPages(socket: RegisteredAgentSocket) {
  const products: PluProduct[] = [];

  for (let page = 1; ; page += 1) {
    const acknowledgement = await executeAgentAction<PluPageResult>(socket, {
      actionId: crypto.randomUUID(),
      command: "get_plu_page",
      params: { page, pageSize: 100 },
      timestamp: new Date().toISOString(),
    });

    if (!acknowledgement.ok) {
      throw new Error(acknowledgement.error);
    }

    products.push(...acknowledgement.result.products);
    if (page >= acknowledgement.result.totalPages) {
      return products;
    }
  }
}
```

## Session recovery, pushes, and errors

The backend never performs POS login. For every POS data action, the connector
tries the saved cookie. When it is missing or expired, the connector performs
at most one `validate` login, persists the encrypted replacement cookie, and
performs one retry of the original POS action. If that retry identifies a
second expiry, the action fails with `POS_AUTH_EXPIRED`.

`agent_data_push` exists for explicit future/manual pushes, but it is not automatically
emitted for these four actions. Their data travels only through the
`execute_local_action` acknowledgement.

Errors are safe display text. Consumers may display the complete error, but
must branch only on the prefix before the first colon.

| Prefix | Meaning |
| --- | --- |
| `INVALID_ACTION` | Envelope or action parameters are invalid. |
| `UNSUPPORTED_COMMAND` | The command is not implemented by this connector. |
| `NOT_REGISTERED` | The connector is not registered with the bridge. |
| `SETTINGS_MISSING` | Connector settings are unavailable. |
| `SETTINGS_SAVE_FAILED` | The connector could not persist settings. |
| `POS_AUTH_EXPIRED` | The saved or retried POS session is invalid. |
| `POS_LOGIN_FAILED` | The POS rejected or failed the login. |
| `POS_TIMEOUT` | The POS did not respond before the deadline. |
| `POS_CERTIFICATE_CHANGED` | The configured POS certificate no longer matches. |
| `POS_CERTIFICATE_UNTRUSTED` | The POS certificate is not trusted. |
| `POS_HTTP_ERROR` | The POS data request received a non-success response. |
| `POS_UNSUPPORTED_CONTENT_ENCODING` | The POS response uses an unsupported encoding. |
| `POS_INVALID_XML` | The POS response XML is invalid or unsafe. |
| `POS_INVALID_RESPONSE` | The POS response does not match the expected contract. |
| `PAYLOAD_TOO_LARGE` | The successful acknowledgement exceeds the bridge limit. |
| `COMMAND_CANCELLED` | The connector is shutting down. |
| `INTERNAL_ERROR` | The connector could not complete the command. |

## Security checklist

- Authorize every browser request before registered-agent lookup.
- Keep POS credentials, cookies, full licenses, raw XML, and internal socket
  identities out of browser responses, URLs, analytics, and logs.
- Use only the fixed `https://connector.retwho.com` bridge and `/socket.io`
  path for connector traffic.
- Treat the POS request references above as connector-to-POS only—not
  browser-callable—and do not recreate them in TypeScript.
- Reuse an action ID only when retrying the same logical operation; generate a
  new one for a different page or command.
- Cap client and backend responses below the connector's one-MiB success limit.

## Web-backend test checklist

- Use fake registered sockets and fake data only; do not contact a POS or the
  production bridge.
- Assert that a successful action receives exactly one acknowledgement.
- Assert the 10-second Socket.IO timeout and malformed acknowledgement paths.
- Assert duplicate delivery reuses the same action ID and does not create a
  second logical operation.
- Assert pagination makes one `get_plu_page` action per page and stops at
  `totalPages`.
- Assert `POS_AUTH_EXPIRED` and the other prefixes are classified from the
  text before the first colon, while the full safe text is displayable.
- Assert browser DTOs omit `rawXml`, credentials, cookies, POS origins, and
  internal connector/license identities.
