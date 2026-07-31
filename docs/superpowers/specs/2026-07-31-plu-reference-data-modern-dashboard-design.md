# PLU, Referential Data, and Modern Dashboard Design

## Objective

Extend Hybrid Edge Connector Agent with three bounded Socket.IO commands that
read Sapphire/Commander NAXML product and referential data:

- get_plu_page
- get_plu
- get_referential_integrity

The connector parses the returned XML locally and acknowledges each command
with normalized camelCase JSON. The existing get_current_data contract,
origin-bound certificate trust, DPAPI settings, one-time session refresh,
duplicate-action sharing, exactly-once acknowledgement, payload limits, and
secret-safe logging remain in force.

The WPF dashboard also adopts the approved compact status-rail design, corrects
every white-on-white text condition, and gives each command button a distinct
modern, accessible style.

## Scope

This change includes:

1. one-page-at-a-time PLU retrieval;
2. exact UPC and UPC-modifier lookup;
3. retrieval of the fixed referential datasets;
4. secure XML-to-typed-model mapping;
5. normalized bridge JSON;
6. the existing one-login/one-retry recovery for all POS data commands; and
7. the approved compact WPF status rail and contrast/button improvements.

This change does not include fetching all PLU pages in one action, modifying
POS data, arbitrary NAXML commands, arbitrary referential datasets, automatic
polling, automatic pushes, raw XML bridge results for the new commands,
certificate-validation bypass, or credential/payload logging.

## Bridge Commands

### get_plu_page

Parameters:

    {
      "page": 1,
      "pageSize": 100
    }

Both properties are optional. page defaults to 1 and must be a positive 32-bit
integer. pageSize defaults to 100 and must be an integer from 1 through 100.
Each cloud action retrieves exactly one POS page. The cloud requests
subsequent pages using the totalPages value returned by the previous response.

### get_plu

Parameters:

    {
      "upc": "00000000000001",
      "upcModifier": "000"
    }

upc is required, must contain 1 through 32 decimal digits, and is sent exactly
as supplied so leading zeroes are preserved. The connector does not substitute
or left-pad a missing UPC. upcModifier is optional, defaults to 000, and must
contain exactly three decimal digits.

### get_referential_integrity

Parameters are an empty object. The connector always requests all six approved
datasets in this fixed order:

    prodCodes,departments,ageValidations,taxRates,blueLaws,fees

The cloud cannot add, remove, reorder, or inject dataset names.

### Common action behavior

All commands retain the existing action requirements: a safe bounded actionId,
object-valued params, and non-default timestamp. For the three new commands,
unknown properties, invalid JSON types, missing required properties, and
out-of-range values return a safe INVALID_ACTION acknowledgement without
contacting the POS. Existing get_current_data parameter behavior does not
change.

## Exact POS Request Formats

All operations use the existing POS-only IPosHttpClient, HTTP/1.1
compatibility headers, TLS 1.2/1.3, normal Windows trust or explicit
origin-bound SHA-256 pinning, two-MiB decompressed response limit, and
credential-safe diagnostics.

### PLU page

The POST URI query contains encoded cmd=vPLUs and the active cookie. The UTF-8
text/plain body contains the same encoded command line, one empty line, and
the generated XML selector:

    cmd=vPLUs&cookie=FAKE_COOKIE

    <domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01"><pageSize>100</pageSize><page>1</page></domain:PLUSelect>

The body uses UTF-8 without a byte-order mark and the exact separator
CRLF+CRLF between the form line and XML. Query values are URI encoded. The XML
is generated with XML APIs rather than interpolated from untrusted text.

### Single PLU

The query and first body line match the PLU-page request. The generated
selector adds an exact where condition:

    <domain:PLUSelect xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01"><query><where><upc source="keyboard">00000000000001</upc><upcModifier>000</upcModifier></where></query><pageSize>100</pageSize><page>1</page></domain:PLUSelect>

The single-product selector always uses page 1 and page size 100.

### Referential integrity

The POST URI query and UTF-8 text/plain body both contain only:

    cmd=vrefinteg&dataset=prodCodes,departments,ageValidations,taxRates,blueLaws,fees&cookie=FAKE_COOKIE

No XML or trailing separator follows the form line. Because the dataset value
is a fixed connector constant rather than cloud input, its comma separators
remain literal in both the query and body. The cookie remains URI encoded.
Exact bytes are locked by request tests.

The request URI and body are credential-bearing and must never be written to
UI, file, SQLite, framework, or exception diagnostics.

## Components and Boundaries

### POS request factory

PosHttpRequestFactory gains explicit methods for PLU-page, single-PLU, and
referential-integrity requests. A small internal body builder supports the two
approved layouts without weakening the existing login and vdatetime formats.
Every request continues to carry the configured-origin, certificate-pin,
certificate-decision, and safe command-name options.

### POS data service

IPosDataService gains typed asynchronous methods for the three operations.
PosDataService owns HTTP status handling and common authentication-fault
classification. Dedicated mappers own only XML validation and normalized model
construction:

- PluXmlMapper maps both page and exact-product responses;
- ReferentialIntegrityXmlMapper maps the six fixed datasets; and
- existing VdatetimeXmlMapper remains unchanged.

All mappers use the existing DTD-prohibited, resolver-free,
namespace-independent, bounded XML reader. Expected root local names are PLUs
and referentialIntegrity.

### Coordinator

ConnectorCoordinator remains the only bridge-command executor. Command
dispatch selects one typed POS operation after parameter validation. A generic
internal one-refresh helper replaces the vdatetime-specific duplicate flow so
every operation follows the same rules:

1. use the saved cookie when available;
2. execute the selected operation;
3. on typed session expiry, call validate once;
4. atomically save the encrypted replacement cookie;
5. retry the same operation once; and
6. return the result or the second failure without another login.

The existing POS-session semaphore serializes POS work. The shared action
deadline, application/session cancellation, duplicate action registry,
one-MiB bridge JSON limit, and exactly-once acknowledgement wrapper remain.

## Normalized JSON Models

JSON uses the shared camelCase, null-omitting serializer. UPCs, modifiers, and
all POS identifiers remain strings. Monetary and quantity values are decimal
JSON numbers. Missing optional scalar fields are omitted; collection
properties are returned as empty arrays.

### PLU page result

    {
      "source": "NAXML",
      "command": "vPLUs",
      "page": 1,
      "totalPages": 68,
      "requestedPageSize": 100,
      "itemCount": 1,
      "products": [
        {
          "upc": "00000000000001",
          "upcModifier": "000",
          "description": "FAKE PRODUCT",
          "departmentId": "19",
          "feeIds": ["0"],
          "productCode": "0",
          "price": 4.67,
          "flagIds": [],
          "taxRateIds": ["1"],
          "idCheckIds": ["2"],
          "sellUnit": 1.000,
          "taxableRebateAmount": 0.00,
          "groupCodes": [],
          "maxQuantityPerTransaction": 0.00
        }
      ],
      "fetchedAtUtc": "2026-07-31T00:00:00Z"
    }

page and totalPages come from the PLUs root attributes. itemCount is computed
from mapped PLU elements.

### Single PLU result

The result contains source, command, requestedUpc, requestedUpcModifier, found,
product, and fetchedAtUtc. An empty valid PLUs response returns ok=true,
found=false, and omits product. More than one exact match is an invalid POS
response rather than an arbitrary first-item selection.

### Referential-integrity result

The result contains:

- source, command, siteId, and fetchedAtUtc;
- record limits from the dataset containers;
- normalized taxRates;
- normalized departments;
- normalized productCodes;
- normalized ageValidations;
- normalized fees; and
- normalized blueLaws.

Known fields from the supplied fixture are preserved, including sysid, name,
isFuel, and optional department prodCode. Boolean-like 0/1 attributes are
validated strictly and exposed as JSON booleans. Empty fee or blue-law
datasets become empty arrays while their advertised limits remain available.

The supplied response has no fee or blue-law child records. To avoid silently
dropping records returned by another supported Commander configuration, a
non-empty fee or blue-law child is normalized as a reference definition with
recordType, optional id/name, and a camelCase fields object containing its
remaining direct attributes and scalar child values. Namespace prefixes,
attribute marker syntax, and XML text markers are not exposed. Nested
unsupported structures fail with POS_INVALID_RESPONSE instead of returning
partial data.

The new results do not contain rawXml, cookies, POS origins, request details,
or diagnostic metadata.

## Session Expiry and Error Handling

Every operation classifies HTTP 401/403 as POS_AUTH_EXPIRED. A successful HTTP
XML fault is also classified as expired when its namespace-independent
faultCode is CGIPortal.LoginRequired, or when it meets the existing bounded
authentication-subject plus failure-indicator rule.

A retry that returns another authentication fault maps to POS_AUTH_EXPIRED; it
cannot trigger another login. Other valid unexpected roots map to
POS_INVALID_RESPONSE. Malformed or unsafe XML maps to POS_INVALID_XML.
Non-success HTTP status, transport/TLS failure, timeout, oversized decompressed
response, and excessive serialized bridge result retain their existing safe
error mappings.

No POS response can bypass certificate validation, extend the shared command
deadline, or create an unbounded retry.

## Safe Diagnostics

Safe diagnostic entries may contain:

- bridge command and action ID;
- POS command name;
- attempt stage without cookie data;
- duration;
- HTTP status and bounded safe response metadata;
- response byte/character length;
- XML root local name;
- bounded, sanitized fault code/string/message; and
- result classification and mapped record counts.

Diagnostics must not contain request URIs, query strings, request bodies,
cookies, credentials, license values, raw XML, individual products,
referential records, raw Socket.IO payloads, or unrestricted exception
messages/stack traces. Microsoft framework HTTP logging remains removed from
the POS client.

## Compact WPF Status Rail

The main dashboard replaces four separated status boxes with one compact,
segmented status rail. It retains four logical segments:

1. Configuration;
2. Server;
3. Agent; and
4. Logs.

Each segment contains a vector icon, accessible name, semantic state text,
short description, and visible signal light:

- green: configured, registered/connected, active, or healthy;
- yellow: testing, connecting/registering/reconnecting, waiting/refreshing,
  or degraded; and
- red: missing/invalid, disconnected/permanent rejection, agent error/session
  replacement, or stopped logging.

State is never communicated by color alone. Narrow windows wrap the rail into
a two-by-two layout without clipping status text.

The command bar uses separate styles:

- neutral secondary Settings;
- green primary Connect, with a distinct Disconnect state;
- blue outlined Open Logs;
- red danger-outline Exit.

Each button has a rounded control template, vector icon, explicit foreground
and background, hover, pressed, disabled, and keyboard-focus states. Icons do
not depend on emoji font rendering.

The theme audit covers every main-window and configuration-dialog title,
description, label, validation message, checkbox, text box, password box,
button, disabled state, and dialog surface. Explicit foreground/background
pairs prevent white-on-white text. Focus indicators, automation names,
keyboard navigation, and high-contrast behavior remain mandatory.

## Tests

All fixtures use only FAKE_* credentials and synthetic product/reference
values. The supplied XML shapes may guide sanitized fixtures, but production
inventory values are not copied into source control. Tests make no production
connections.

Required tests include:

1. exact PLU page query and combined body bytes;
2. exact single-PLU query and selector XML, including escaping;
3. exact fixed-dataset referential query/body;
4. page/page-size defaults, bounds, types, and unknown-property rejection;
5. required numeric UPC and default/validated modifier;
6. namespace-independent parsing of the supplied PLU and referential shapes;
7. every normalized product field, optional field, array, attribute, decimal,
   identifier, page count, and item count;
8. exact-product found, not-found, and multiple-match behavior;
9. strict 0/1 boolean mapping and malformed-data rejection;
10. DTD, malformed XML, unexpected root, response limit, and payload limit;
11. HTTP and XML session expiry for every new command;
12. exactly one login and one retry of the original command;
13. second-expiry termination without another login;
14. duplicate-action sharing and exactly-once acknowledgement;
15. exact camelCase JSON and null omission;
16. proof that request/response XML, cookies, URIs, product values, and
    referential values do not reach logs;
17. status-rail state and accessibility mappings;
18. explicit contrast resources and modern button visual states; and
19. Windows STA construction/layout tests for main and configuration windows.

The complete existing test suite, formatting verification, Debug and Release
builds, tracked-secret scan, and self-contained untrimmed win-x64 publish
remain completion gates.

## Acceptance Limits

Automated tests validate request construction, parsers, bridge contracts,
session recovery, safe diagnostics, and WPF resource behavior without
contacting production systems.

A Windows machine with the .NET 10 desktop SDK is required to validate final
WPF rendering, keyboard/high-contrast behavior, Visual Studio solution launch,
DPAPI, tray lifecycle, and the published executable. Dedicated test POS and
bridge credentials are required to verify the live request-body line breaks,
Commander response variants, registration, page retrieval, exact UPC lookup,
referential retrieval, expiry refresh, and Socket.IO acknowledgements. Missing
live credentials must be reported rather than bypassed.
