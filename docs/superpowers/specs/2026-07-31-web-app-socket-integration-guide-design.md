# Web-App Socket Integration Guide Design

## Purpose

Create a root-level `WEB_APP_SOCKET_INTEGRATION_GUIDE.md` for developers of a
Node.js/TypeScript cloud backend that dispatches work to the existing Windows
connector through the fixed Socket.IO bridge. The guide must explain how to
request each implemented POS dataset and consume the connector's exactly-once
acknowledgement without exposing POS credentials or session cookies to a
browser.

The guide documents the implemented connector contract. It does not claim
that a browser-facing REST API already exists, and it does not invent a
second Socket.IO endpoint.

## Audience and Boundary

The primary reader maintains the cloud bridge or a trusted web backend that
can route a request to the registered `localhost_agent` socket for a license.

The supported flow is:

```text
Browser -> trusted web backend/cloud bridge -> registered connector socket
        -> local POS HTTPS/NAXML -> connector acknowledgement -> backend
        -> browser-safe response
```

The browser must never connect to the POS, receive the POS username,
password, XML cookie, certificate pin, or supply an arbitrary POS URL. The
connector owns POS authentication, certificate trust, cookie refresh, XML
parsing, and normalized JSON mapping.

## Fixed Transport Contract

The guide will record the existing agent connection contract:

- URL: `https://connector.retwho.com`
- Socket.IO path: `/socket.io`
- Engine.IO: v4
- transport: WebSocket
- agent handshake auth: `{ licenseKey }`
- registration event: `register_client`
- agent type: `localhost_agent`
- successful registration requires `ok: true` and `code: "REGISTERED"`
- command event delivered to the agent: `execute_local_action`
- terminal duplicate-agent event: `session_replaced`

The TypeScript example represents server-side dispatch through an already
authenticated cloud bridge and its registered-agent registry. It will not
connect a second client using the agent's license or pretend that agent
handshake authentication authorizes a web producer.

## Action Envelope

Every server dispatch uses one JSON object:

```json
{
  "actionId": "FAKE-ACTION-001",
  "command": "get_current_data",
  "params": {},
  "timestamp": "2026-07-31T12:00:00Z"
}
```

The guide will explain:

- `actionId` is required, safe, 1-128 characters, and is the idempotency key;
- `command` is required;
- `params` must be a JSON object;
- `timestamp` is required and non-default;
- duplicate calls with the same `actionId` share one execution and completed
  acknowledgements are retained temporarily;
- command work has an eight-second connector deadline;
- the Socket.IO acknowledgement boundary is ten seconds; and
- a full successful acknowledgement at or above one MiB is rejected.

Acknowledgements use exactly one of these shapes:

```json
{ "ok": true, "result": {} }
```

```json
{ "ok": false, "error": "ERROR_CODE: Safe description." }
```

Null `result` and `error` properties are omitted, and all property names use
camelCase.

## Supported Actions

The guide will contain a dedicated section and a copyable TypeScript call for
each action.

### `get_current_data`

- Parameters: empty object.
- Connector POS request: `vdatetime` with the active cookie.
- Result: NAXML source, command, site, system date/time, time-zone data, exact
  decoded `rawXml`, and UTC fetch time.
- `rawXml` is bridge response data for this action only and must not be copied
  into diagnostic logs.

### `get_plu_page`

- Parameters: optional `page` and `pageSize`.
- Defaults: page 1 and page size 100.
- Limits: page is a positive 32-bit integer; page size is 1-100.
- Connector POS request: `vPLUs` and a `PLUSelect` XML selector.
- Result: one page only, including page, total pages, requested page size,
  item count, normalized products, and UTC fetch time.
- The cloud backend owns pagination and generates a new `actionId` for each
  requested page.

### `get_plu`

- Parameters: required 1-32 digit `upc`; optional three-digit `upcModifier`
  defaulting to `000`.
- Connector POS request: `vPLUs` with an exact UPC selector.
- Result: normalized lookup with `found`; a valid empty result returns
  `ok: true`, `found: false`, and omits `product`.

### `get_referential_integrity`

- Parameters: empty object only.
- Connector POS request: `vrefinteg` with the fixed ordered dataset list
  `prodCodes,departments,ageValidations,taxRates,blueLaws,fees`.
- Result: site, all six per-dataset limits, tax rates, departments, product
  codes, age validations, fees, blue laws, and UTC fetch time.
- The backend cannot add, remove, reorder, or inject dataset names.

## POS URL Reference

For troubleshooting, each action section will show the internal HTTPS path
and request-body shape used by the connector. Every example uses placeholders
such as `https://POS_HOST` and `FAKE_COOKIE`; none is a browser-callable API.

The guide will explicitly label these URLs as connector-to-POS traffic:

- `validate`
- `vdatetime`
- `vPLUs` page selector
- `vPLUs` exact lookup selector
- `vrefinteg` fixed datasets

The TypeScript backend never builds these POS URLs and never handles their
credential-bearing query or form data.

## TypeScript Server Example

The guide will provide a framework-light Socket.IO server helper centered on
an existing registered-agent lookup:

```ts
executeAgentAction(licenseId, action): Promise<AgentAck>
```

It will demonstrate:

1. locating one registered `localhost_agent` without exposing a full license;
2. emitting `execute_local_action` with a ten-second acknowledgement timeout;
3. validating the acknowledgement discriminated union;
4. returning safe result data to the caller;
5. mapping connector errors to the web backend's own HTTP/API policy; and
6. generating a unique action ID once per logical operation and reusing it
   only for safe delivery retries of that same operation.

Any illustrative browser-facing route is marked as an example that the web
application must implement, not an existing Retwho endpoint.

## Session Recovery and Errors

The guide will explain that the backend does not log in to the POS. When the
saved cookie is missing or expired, the connector performs at most one
`validate` login, saves the encrypted replacement cookie, and retries the
original POS action once. A second expiry returns `POS_AUTH_EXPIRED`.

It will include a concise table for implemented safe errors, including
invalid action/parameters, unsupported command, missing settings, POS
authentication/transport/XML/response failures, certificate changes,
timeouts/cancellation, oversized payloads, registration failures, session
replacement, temporary busy state, and internal errors. Consumers must treat
the full error string as diagnostic-safe text and may branch on the prefix
before the first colon.

## Security Rules

All examples use `FAKE_*` or synthetic values. The guide will prohibit:

- sending license keys, POS credentials, cookies, pins, or raw authorization
  data to a browser;
- logging command payloads that could contain future sensitive fields;
- exposing connector-to-POS URLs as public web routes;
- accepting arbitrary command names or referential dataset strings;
- automatically retrying with a new action ID after an unknown delivery
  outcome; and
- treating transport-connected as registered.

## Verification

Add documentation contract tests that require the final guide to include:

- fixed bridge URL/path and event names;
- the complete action envelope and acknowledgement shapes;
- all four commands and parameter/default rules;
- internal POS paths with an explicit non-browser warning;
- TypeScript dispatch and pagination examples;
- one-login/one-retry behavior;
- exactly-once/idempotency and timeout/size limits;
- safe error handling and security prohibitions; and
- an explicit statement that example REST routes are not deployed endpoints.

Run focused documentation tests, the full Release suite, Debug and Release
builds, formatting verification, diff checks, and secret/placeholder scans.

## Deliverable

Create and commit:

```text
WEB_APP_SOCKET_INTEGRATION_GUIDE.md
```

No production C#, Socket.IO behavior, POS protocol, public REST service, or
separate sample application is added by this documentation task.
