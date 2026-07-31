# POS Session Refresh and Safe Diagnostics Design

## Objective

When Sapphire/Commander returns the namespaced NAXML fault
`CGIPortal.LoginRequired` for `vdatetime`, the connector must classify the
saved cookie as expired, log in exactly once, encrypt and save the replacement
XML cookie, and retry `vdatetime` exactly once.

POS diagnostics must make failures actionable without writing a password,
username, cookie, license, credential-bearing URI/body, authorization value,
raw login response, or unrestricted POS XML to any UI, file, SQLite, or
framework log.

## Root Causes

The observed response is a successful HTTP XML document whose fault code is:

```text
CGIPortal.LoginRequired
```

`PosDataService` currently recognizes HTTP 401/403 and XML text containing
both one authentication subject and one of `expired`, `invalid`,
`unauthorized`, or `denied`. The observed fault contains `Credential` and
`login`, but none of those failure indicators. The XML mapper therefore
throws `POS_INVALID_RESPONSE`, so `ConnectorCoordinator` never receives the
`PosAuthenticationException` that activates its existing one-refresh path.

Separately, `IHttpClientFactory` installs Microsoft framework HTTP loggers.
Those loggers run outside `PosHttpClient` and generated the supplied full
exception stack. They duplicate the connector's bounded diagnostic pipeline
and are not appropriate for credential-bearing POS requests.

The TLS exception itself means the POS certificate callback rejected the
certificate. It must be reported accurately, but certificate validation must
not be bypassed or automatically approved.

## Session-Fault Classification

`PosDataService` will securely parse successful but unexpected XML using the
existing DTD-prohibited, resolver-free, bounded XML reader.

It will classify the response as `POS_AUTH_EXPIRED` when either:

1. the existing bounded authentication-subject plus failure-indicator rule
   matches; or
2. an element whose local name is `faultCode` has the trimmed,
   case-insensitive value `CGIPortal.LoginRequired`.

Namespace prefixes are irrelevant; classification uses `Name.LocalName`.
Malformed XML remains `POS_INVALID_XML`. Other valid but unexpected XML
remains `POS_INVALID_RESPONSE`. The general keyword set will not be broadened
to every occurrence of `login`, avoiding false session refreshes for
unrelated POS faults.

`ConnectorCoordinator` will retain its existing shared eight-second deadline
and session semaphore. On the new typed expiry:

1. call `validate` once;
2. extract the new cookie only from successful credential XML;
3. atomically save the encrypted new cookie while retaining the origin-bound
   certificate pin;
4. retry `vdatetime` once; and
5. return success, or return the second typed failure without another login.

No polling, unbounded retry, or cloud contract change is introduced.

## Safe POS Diagnostics

Every POS operation will use the existing channel logging pipeline and
sanitizer. Microsoft `IHttpClientFactory` loggers will be removed from the
POS client so they cannot emit duplicate request information or full stack
traces.

Safe request diagnostics may contain only:

- operation/command (`validate` or `vdatetime`);
- method;
- HTTP version;
- declared content byte length;
- whether an approved certificate pin is present;
- retry/attempt stage without a cookie value; and
- elapsed time.

They will never contain the request URI, query, body, username, password,
cookie, license, `Referer`, `Origin`, `Host`, or arbitrary headers.

Safe response diagnostics may contain only:

- HTTP status;
- allowlisted normalized response metadata already represented by
  `PosResponseMetadata`;
- response byte/character length;
- XML root local name;
- allowlisted fault fields `faultCode`, `faultString`, and `message`;
- result classification; and
- elapsed time.

Fault values will be sanitized and individually bounded. The combined details
field remains subject to the pipeline's 32-KiB post-sanitization limit.
Malformed, non-XML, or unrelated response bodies will be represented by type,
length, and classification rather than body content. Successful login XML and
successful `vdatetime` raw XML will never be copied into diagnostic logs.
`rawXml` remains available only in the requested bridge result.

This design intentionally does not log unrestricted send/receive payloads.
Those payloads carry passwords, cookies, credentials, and potentially
unreviewed POS data that field-name redaction cannot guarantee to remove.

## TLS Error Classification

The POS certificate callback will set an internal request decision flag when
it rejects a presented certificate. `PosHttpClient` will use that flag rather
than exception-message text:

- rejected request with an approved pin becomes
  `POS_CERTIFICATE_CHANGED`;
- rejected request without an approved pin becomes
  `POS_CERTIFICATE_UNTRUSTED`; and
- other transport/TLS failures remain safe transport failures.

Only the safe code, safe message, command, timing, and exception type may be
logged. The certificate, fingerprint, request URI, and stack trace are not
logged. The operator must reopen Settings, independently verify any changed
fingerprint, and explicitly approve it.

## Tests

All fixtures use `FAKE_*` values and no production connection.

Required regression tests:

1. The exact namespaced `CGIPortal.LoginRequired` XML maps to
   `POS_AUTH_EXPIRED`.
2. The coordinator performs one login, saves the replacement cookie, retries
   `vdatetime`, and returns success.
3. A second login-required response does not cause a second login.
4. An unrelated valid XML fault remains `POS_INVALID_RESPONSE`.
5. Malformed XML remains `POS_INVALID_XML`.
6. Fault diagnostics include the safe root/code/string/message fields.
7. Diagnostics exclude raw cookies, passwords, usernames, full URIs,
   unrestricted XML, and stack traces.
8. The POS `HttpClient` registration has framework HTTP loggers removed.
9. A callback-rejected request with a pin maps to
   `POS_CERTIFICATE_CHANGED`; without a pin it maps to
   `POS_CERTIFICATE_UNTRUSTED`.
10. Existing certificate matching, HTTP, XML, duplicate-action,
    exactly-once acknowledgement, full test suite, Release build, formatting,
    secret scan, and single-file publish gates remain green.

## Acceptance Limits

Automated tests prove classification, one-refresh behavior, safe diagnostics,
and typed certificate failures. A Windows test with a dedicated POS and
active test license is still required to prove the live device's complete
fault payload, certificate chain, DPAPI save, Socket.IO acknowledgement, and
notification-area behavior.
