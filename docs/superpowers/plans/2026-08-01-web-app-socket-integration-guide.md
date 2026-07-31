# Web-App Socket Integration Guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a complete Node.js/TypeScript backend guide for dispatching every implemented POS action to a registered Windows connector through the fixed Socket.IO bridge.

**Architecture:** Document the trusted browser-to-backend-to-bridge-to-agent-to-POS boundary without inventing a deployed REST API. Use exact current C# contracts as the source of truth, show framework-light TypeScript Socket.IO server acknowledgement code, and clearly separate cloud-to-agent events from connector-to-POS HTTPS URLs.

**Tech Stack:** Markdown, TypeScript examples, Socket.IO v4 server API, C# documentation contract tests, xUnit.

## Global Constraints

- Work directly on `main`, as requested by the user.
- Create only documentation and documentation-test changes; do not change C#, Socket.IO behavior, POS protocol, or application UI.
- The fixed bridge is `https://connector.retwho.com` with path `/socket.io`, Engine.IO v4, and WebSocket transport.
- The guide targets a trusted Node.js/TypeScript web backend or the cloud bridge itself, not an untrusted browser Socket.IO client.
- Do not claim any example browser-facing REST route is already deployed.
- All examples use `FAKE_*`, `EXAMPLE_*`, or synthetic identifiers; never copy runtime licenses, credentials, cookies, POS origins, or uploaded XML.
- `get_current_data`, `get_plu_page`, `get_plu`, and `get_referential_integrity` return through the `execute_local_action` acknowledgement. They are not automatically duplicated through `agent_data_push`.
- Preserve exact camelCase JSON, null omission, action validation, one-login/one-retry, duplicate sharing, exactly-once acknowledgement, eight-second work deadline, ten-second acknowledgement boundary, and one-MiB full-acknowledgement limit.

---

### Task 1: Write and verify the web-app Socket integration guide

**Files:**
- Create: `WEB_APP_SOCKET_INTEGRATION_GUIDE.md`
- Modify: `tests/RetwhoConnector.Tests/DocumentationTests.cs`
- Modify: `tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj`

**Interfaces:**
- Consumes: current `BridgeAction`, `BridgeAcknowledgement`, product, datetime, and referential result contracts serialized by `ConnectorJson.Options`.
- Produces: a backend-developer contract for `execute_local_action` and its exactly-once Socket.IO acknowledgement.

- [ ] **Step 1: Link the future guide into the test output**

Add this item beside the existing README fixture in
`RetwhoConnector.Tests.csproj`:

```xml
<None Include="..\..\WEB_APP_SOCKET_INTEGRATION_GUIDE.md"
      Link="Fixtures\WEB_APP_SOCKET_INTEGRATION_GUIDE.md"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Add failing documentation contracts**

Add this helper and focused tests to `DocumentationTests`:

```csharp
private static string ReadWebAppGuide() =>
    File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "WEB_APP_SOCKET_INTEGRATION_GUIDE.md"));

[Fact]
public void WebAppGuide_DocumentsFixedSocketBoundaryAndAcknowledgements()
{
    string guide = ReadWebAppGuide();

    Assert.StartsWith("# Web-App Socket Integration Guide", guide, StringComparison.Ordinal);
    Assert.Contains("https://connector.retwho.com", guide, StringComparison.Ordinal);
    Assert.Contains("`/socket.io`", guide, StringComparison.Ordinal);
    Assert.Contains("`execute_local_action`", guide, StringComparison.Ordinal);
    Assert.Contains("`register_client`", guide, StringComparison.Ordinal);
    Assert.Contains("`localhost_agent`", guide, StringComparison.Ordinal);
    Assert.Contains("`session_replaced`", guide, StringComparison.Ordinal);
    Assert.Contains("exactly once", guide, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("10 seconds", guide, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("one MiB", guide, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("not a deployed Retwho endpoint", guide, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void WebAppGuide_DocumentsEveryActionAndPosRequestBoundary()
{
    string guide = ReadWebAppGuide();

    foreach (string command in new[]
             {
                 "get_current_data",
                 "get_plu_page",
                 "get_plu",
                 "get_referential_integrity",
             })
    {
        Assert.Contains($"`{command}`", guide, StringComparison.Ordinal);
    }

    Assert.Contains("pageSize` defaults to `100", guide, StringComparison.Ordinal);
    Assert.Contains("upcModifier` defaults to `000", guide, StringComparison.Ordinal);
    Assert.Contains(
        "prodCodes,departments,ageValidations,taxRates,blueLaws,fees",
        guide,
        StringComparison.Ordinal);
    Assert.Contains("vdatetime", guide, StringComparison.Ordinal);
    Assert.Contains("vPLUs", guide, StringComparison.Ordinal);
    Assert.Contains("vrefinteg", guide, StringComparison.Ordinal);
    Assert.Contains("connector-to-POS", guide, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("browser must never", guide, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void WebAppGuide_DocumentsTypeScriptDispatchRecoveryAndPagination()
{
    string guide = ReadWebAppGuide();

    Assert.Contains("executeAgentAction", guide, StringComparison.Ordinal);
    Assert.Contains("socket.timeout(10_000)", guide, StringComparison.Ordinal);
    Assert.Contains("for (let page = 1;", guide, StringComparison.Ordinal);
    Assert.Contains("one `validate` login", guide, StringComparison.Ordinal);
    Assert.Contains("one retry of the original POS action", guide, StringComparison.Ordinal);
    Assert.Contains("POS_AUTH_EXPIRED", guide, StringComparison.Ordinal);
    Assert.Contains("agent_data_push", guide, StringComparison.Ordinal);
    Assert.Contains("not automatically", guide, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Run the documentation tests and verify red**

Run:

```bash
dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj \
  --filter FullyQualifiedName~WebAppGuide
```

Expected: FAIL because `WEB_APP_SOCKET_INTEGRATION_GUIDE.md` does not exist in
the test output.

- [ ] **Step 4: Create the guide header, architecture, and contract ownership**

Create `WEB_APP_SOCKET_INTEGRATION_GUIDE.md` beginning with:

```markdown
# Web-App Socket Integration Guide

This guide is for the trusted Node.js/TypeScript backend or cloud bridge that
routes commands to a registered Hybrid Edge Connector Agent. A browser must
never connect directly to the local POS or receive its credentials or cookie.
```

Include a Mermaid or text sequence showing:

```text
Browser -> Web backend/cloud bridge -> execute_local_action
        -> Windows connector -> HTTPS/NAXML POS
        -> Socket.IO acknowledgement -> Web backend -> browser-safe response
```

Add an **Existing contract versus example application routes** callout:

- the fixed Socket.IO agent contract is implemented;
- the cloud bridge must already have a registered-agent registry;
- any route such as `POST /api/connectors/:connectorId/actions` is an example
  route the reader may implement and is not a deployed Retwho endpoint; and
- the agent license handshake must not be reused as browser authentication.

- [ ] **Step 5: Document connection, registration, routing, and action schemas**

Document the fixed agent transport and registration values from the global
constraints. Show the exact action envelope:

```json
{
  "actionId": "FAKE-ACTION-001",
  "command": "get_current_data",
  "params": {},
  "timestamp": "2026-08-01T00:00:00Z"
}
```

Document the two acknowledgement shapes:

```json
{ "ok": true, "result": {} }
```

```json
{ "ok": false, "error": "ERROR_CODE: Safe description." }
```

State that `actionId` is the 1-128 character idempotency key, `params` must be
an object, timestamp is required, JSON is camelCase with null omission,
duplicates share one execution, the connector deadline is eight seconds, the
Socket.IO acknowledgement boundary is 10 seconds, and the entire successful
acknowledgement must remain below one MiB.

- [ ] **Step 6: Add the framework-light TypeScript dispatch helper**

Include compile-oriented types and this server-side pattern, adapting imports
to `socket.io` while keeping the behavior intact:

```ts
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

Define `RegisteredAgentSocket` as the trusted socket returned by the cloud
bridge's registered-agent lookup. Explain that its lookup key is an internal
connector/license identity and that full license values must not appear in
browser URLs, logs, or analytics.

- [ ] **Step 7: Document all four action calls and normalized results**

For each action, include a synthetic action envelope, parameter rules, a
TypeScript result type, an abbreviated successful acknowledgement, and the
connector-to-POS request reference.

Required connector-to-POS references:

```text
POST https://POS_HOST/cgi-bin/NAXML?cmd=validate&user=FAKE_USER&passwd=REDACTED
POST https://POS_HOST/cgi-bin/NAXML?cmd=vdatetime&cookie=FAKE_COOKIE
POST https://POS_HOST/cgi-bin/NAXML?cmd=vPLUs&cookie=FAKE_COOKIE
POST https://POS_HOST/cgi-bin/NAXML?cmd=vrefinteg&dataset=prodCodes,departments,ageValidations,taxRates,blueLaws,fees&cookie=FAKE_COOKIE
```

Label every URL **connector-to-POS only—not browser-callable**. Show the page
and exact-lookup `PLUSelect` XML bodies using only synthetic UPC values. State
that query/form credentials and cookies are shown only as redacted structural
references and are built by the Windows connector, not TypeScript.

The referential acknowledgement must show all six `limits` entries, including
`blueLaws: { "maxRecords": 0 }`, plus arrays for tax rates, departments,
product codes, age validations, fees, and blue laws.

- [ ] **Step 8: Add backend-owned PLU pagination**

Include a TypeScript function whose loop contains the documented marker and
uses a new action ID for each page:

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

Explain that one bridge action retrieves exactly one POS page and a new
`actionId` identifies each distinct page operation. If delivery outcome is
unknown, a retry of the same logical page reuses its original action ID.

- [ ] **Step 9: Document session recovery, errors, pushes, and security**

Explain that the backend never performs POS login. The connector tries the
saved cookie, performs at most one `validate` login when missing/expired,
persists the encrypted replacement, and performs one retry of the original POS
action. A second expiry returns `POS_AUTH_EXPIRED`.

Include a concise error-prefix table covering:

```text
INVALID_ACTION
UNSUPPORTED_COMMAND
NOT_REGISTERED
SETTINGS_MISSING
SETTINGS_SAVE_FAILED
POS_AUTH_EXPIRED
POS_LOGIN_FAILED
POS_TIMEOUT
POS_CERTIFICATE_CHANGED
POS_CERTIFICATE_UNTRUSTED
POS_HTTP_ERROR
POS_UNSUPPORTED_CONTENT_ENCODING
POS_INVALID_XML
POS_INVALID_RESPONSE
PAYLOAD_TOO_LARGE
COMMAND_CANCELLED
INTERNAL_ERROR
```

Tell consumers to treat the full error as safe display text and branch only
on the prefix before the first colon.

Document that `agent_data_push` exists for explicit future/manual pushes but
is not automatically emitted for these four actions; their data travels only
through the `execute_local_action` acknowledgement. State that
`session_replaced` is terminal for the older agent until manual reconnection.

Finish with a security checklist and a web-backend test checklist using fake
sockets and fake data only.

- [ ] **Step 10: Run focused documentation tests and verify green**

Run:

```bash
dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj \
  --filter FullyQualifiedName~WebAppGuide
```

Expected: all three new documentation tests pass.

- [ ] **Step 11: Run full verification**

Run:

```bash
dotnet restore RetwhoConnector.sln --locked-mode
dotnet build RetwhoConnector.sln -c Debug --no-restore
dotnet build RetwhoConnector.sln -c Release --no-restore
dotnet test RetwhoConnector.sln -c Release --no-build
dotnet format RetwhoConnector.sln --verify-no-changes --no-restore
git diff --check
```

Expected: zero-warning builds, complete test success, clean formatting, and no
diff whitespace errors.

- [ ] **Step 12: Scan examples and commit**

Run targeted scans:

```bash
git diff --check
git diff -- WEB_APP_SOCKET_INTEGRATION_GUIDE.md \
  tests/RetwhoConnector.Tests/DocumentationTests.cs \
  tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj
git grep -nE '3ba5d0[8]1|cookie=[0-9a-fA-F]{16,}|NotImplementedExceptio[n]|T[O]DO|T[B]D' \
  -- WEB_APP_SOCKET_INTEGRATION_GUIDE.md
```

Expected: the guide contains no copied cookie, runtime secret, placeholder, or
unfinished implementation marker. Confirm example routes are labeled as not
deployed endpoints, then commit:

```bash
git add WEB_APP_SOCKET_INTEGRATION_GUIDE.md \
  tests/RetwhoConnector.Tests/DocumentationTests.cs \
  tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj
git commit -m "docs: add web app socket integration guide"
```
