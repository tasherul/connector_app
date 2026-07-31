# POS Session Refresh and Safe Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recognize Sapphire `CGIPortal.LoginRequired` responses, perform exactly one cookie refresh, and persist actionable POS request/response diagnostics without logging credentials, unrestricted XML, or framework stack traces.

**Architecture:** Add one internal XML-fault inspector shared by POS transport diagnostics and session classification. Keep retry ownership in `ConnectorCoordinator`, add an internal certificate-decision holder to communicate callback rejection safely, and disable `IHttpClientFactory`'s duplicate framework loggers for the POS client.

**Tech Stack:** C# 14, .NET 10 Windows, WPF Generic Host, `HttpClient`, secure LINQ to XML, `System.Text.Json`, xUnit.

## Global Constraints

- Keep `net10.0-windows`, nullable analysis, and warnings-as-errors.
- Do not bypass POS certificate validation or change cloud TLS.
- Never log request URIs, query strings, bodies, passwords, usernames, cookies, licenses, authorization values, unrestricted XML, or stack traces.
- Keep the shared eight-second command deadline, one session semaphore, one login refresh, one retry, and exactly-once acknowledgement.
- Use only `FAKE_*` secrets in tests; tests never contact the real POS or bridge.
- Preserve the bridge acknowledgement contract and exact successful `rawXml`.

---

### Task 1: Classify the Sapphire Login-Required Fault

**Files:**
- Create: `src/RetwhoConnector.Core/Services/PosXmlFaultInspector.cs`
- Create: `tests/RetwhoConnector.Tests/Fixtures/pos-login-required.xml`
- Modify: `src/RetwhoConnector.Core/Services/PosDataService.cs`
- Modify: `tests/RetwhoConnector.Tests/PosProtocolTests.cs`

**Interfaces:**
- Produces: `PosXmlFaultInspector.TryInspect(string xml, out PosXmlFaultDetails? details)` and `PosXmlFaultDetails.IsLoginRequired`.
- Consumes: `SecureXml.Parse(string)` so DTD and size protections remain authoritative.

- [ ] **Step 1: Add the exact safe fixture and failing classification tests**

Add the supplied namespaced response with no credentials:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<VFI:Response xmlns:VFI="urn:vfi-sapphire:np.domain.2001-07-01">
  <VFI:Fault>
    <faultCode>CGIPortal.LoginRequired</faultCode>
    <faultString>CGIPortal Error</faultString>
    <detail>
      <e:vfiFault xmlns:e="urn:vfi-sapphire:np.domain.2001-07-01">
        <e:message>No Credential for the User. Please login to get the Credential</e:message>
      </e:vfiFault>
    </detail>
  </VFI:Fault>
</VFI:Response>
```

Add `PosData_LoginRequiredFaultMapsToSessionExpiry` and
`PosData_UnrelatedFaultRemainsInvalidResponse`. The first expects
`POS_AUTH_EXPIRED`; the second sends a valid `CGIPortal.InvalidCommand` fault
and expects `POS_INVALID_RESPONSE`.

- [ ] **Step 2: Run the targeted tests and verify RED**

Run:

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~PosData_LoginRequiredFaultMapsToSessionExpiry|FullyQualifiedName~PosData_UnrelatedFaultRemainsInvalidResponse"
```

Expected: login-required test fails with `POS_INVALID_RESPONSE`; unrelated
fault already remains invalid.

- [ ] **Step 3: Implement the minimal secure fault inspector**

Create an internal immutable details type containing only `RootName`,
`FaultCode`, `FaultString`, and `Message`. Find elements by `Name.LocalName`.
Set `IsLoginRequired` only when trimmed `FaultCode` equals
`CGIPortal.LoginRequired`, ignoring case.

Update `LooksLikeSessionExpiry` to parse once, check the exact fault code,
then apply the existing text heuristic. Do not add broad `login` keywords.

- [ ] **Step 4: Run targeted POS protocol tests and verify GREEN**

Run the Task 1 filter, then:

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~PosProtocol"
```

- [ ] **Step 5: Commit**

```bash
git add src/RetwhoConnector.Core/Services/PosXmlFaultInspector.cs src/RetwhoConnector.Core/Services/PosDataService.cs tests/RetwhoConnector.Tests/Fixtures/pos-login-required.xml tests/RetwhoConnector.Tests/PosProtocolTests.cs
git commit -m "fix: refresh Sapphire login-required sessions"
```

### Task 2: Prove Exactly One Refresh for the Real Fault

**Files:**
- Modify: `tests/RetwhoConnector.Tests/ExecutionAndBridgeTests.cs`

**Interfaces:**
- Consumes: `ConnectorCoordinator.HandleActionAsync`, existing
  `IPosAuthenticationService`, and existing `IPosDataService`.
- Produces: regression coverage that the first typed fault refreshes and the
  second typed fault cannot loop.

- [ ] **Step 1: Add two coordinator tests**

Add a fake data-service mode that throws `POS_AUTH_EXPIRED` on the first call
or every call. Assert:

```text
first-call-only: authentication=1, data=2, savedCookie=FAKE_NEW_COOKIE, ack ok
every-call: authentication=1, data=2, ack error starts POS_AUTH_EXPIRED
```

- [ ] **Step 2: Run both coordinator characterization tests**

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~Coordinator_ExpiredCookie|FullyQualifiedName~Coordinator_SecondLoginRequired"
```

Expected: both pass with one authentication call and two data calls; no
production coordinator change is required unless the test disproves the
existing control flow.

- [ ] **Step 3: Commit**

```bash
git add tests/RetwhoConnector.Tests/ExecutionAndBridgeTests.cs
git commit -m "test: prevent repeated POS session refresh"
```

### Task 3: Add Bounded Safe Send/Receive Diagnostics

**Files:**
- Modify: `src/RetwhoConnector.Core/Services/PosHttpClient.cs`
- Modify: `src/RetwhoConnector.Core/Services/PosAuthenticationService.cs`
- Modify: `src/RetwhoConnector.Core/Services/PosDataService.cs`
- Modify: `tests/RetwhoConnector.Tests/PosHttpClientTests.cs`

**Interfaces:**
- Consumes: `PosXmlFaultInspector`, `ILogSanitizer`, `IAgentLog`, and
  `PosResponseMetadata`.
- Produces: one sanitized structured detail record per completed or failed
  POS operation.

- [ ] **Step 1: Write failing safe-diagnostic tests**

Update `SendAsync_ReadsBoundedResponseAndLogsOnlySafeMetadata` to require
details containing method, command, HTTP version, content length, status,
root name, and response length.

Add a fault-response test requiring `CGIPortal.LoginRequired`,
`CGIPortal Error`, and its message while asserting absence of:

```text
FAKE_PASSWORD
FAKE_COOKIE
FAKE_USER
https://
<VFI:Response
 at System.
```

Add a transport-failure test that permits only the exception type, safe
request metadata, and elapsed time.

- [ ] **Step 2: Run `PosHttpClientTests` and verify RED**

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~PosHttpClientTests"
```

Expected: current null/minimal details fail the new assertions.

- [ ] **Step 3: Implement immutable diagnostic DTOs inside `PosHttpClient`**

Inject `ILogSanitizer`. Build JSON with nested `request` and `response`
objects only from:

```text
request.command, request.method, request.version,
request.contentLength, request.hasCertificatePin,
response.statusCode, response.reasonPhrase, response.contentType,
response.contentLength, response.contentEncodings,
response.date, response.server, response.connection, response.retryAfter,
hasSetCookieHeader, hasWwwAuthenticateHeader,
responseCharacters, rootName, faultCode, faultString, message,
classification, elapsedMilliseconds, exceptionType
```

Sanitize and cap each XML fault string before serialization. Never serialize
`HttpRequestMessage`, `PosHttpResponse.Body`, an exception object, or a stack
trace. Update internal compatibility constructors to use `LogSanitizer`.

- [ ] **Step 4: Run transport and sanitizer tests**

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~PosHttpClientTests|FullyQualifiedName~LogSanitizerTests"
```

- [ ] **Step 5: Commit**

```bash
git add src/RetwhoConnector.Core/Services/PosHttpClient.cs src/RetwhoConnector.Core/Services/PosAuthenticationService.cs src/RetwhoConnector.Core/Services/PosDataService.cs tests/RetwhoConnector.Tests/PosHttpClientTests.cs
git commit -m "feat: log bounded POS diagnostics"
```

### Task 4: Type Certificate Rejections and Remove Framework HTTP Logs

**Files:**
- Modify: `src/RetwhoConnector.Core/Services/PosHttpRequestFactory.cs`
- Modify: `src/RetwhoConnector.Core/Services/PosHttpClientHandlerFactory.cs`
- Modify: `src/RetwhoConnector.Core/Services/PosHttpClient.cs`
- Modify: `src/RetwhoConnector.App/App.xaml.cs`
- Modify: `tests/RetwhoConnector.Tests/PosHttpClientTests.cs`
- Modify: `tests/RetwhoConnector.Tests/WpfStartupTests.cs`

**Interfaces:**
- Produces: internal `CertificateValidationDecision` stored in request
  options before send, with a thread-safe rejected flag.
- Consumes: existing certificate pin option and
  `PosCertificateException`.

- [ ] **Step 1: Write failing certificate and registration tests**

Add tests where a throwing handler marks the request decision rejected and
throws `HttpRequestException`:

```text
pin present  -> POS_CERTIFICATE_CHANGED
pin absent   -> POS_CERTIFICATE_UNTRUSTED
decision not rejected -> original HttpRequestException
```

Extend the App source test to require `.RemoveAllLoggers()` on the typed POS
client registration.

- [ ] **Step 2: Run targeted tests and verify RED**

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~Certificate|FullyQualifiedName~Application_"
```

- [ ] **Step 3: Implement request decision and typed mapping**

Create the decision holder before send in `PosHttpRequestFactory`. In the
certificate callback, set its rejected flag whenever trust validation
returns false. In `PosHttpClient`, map only a marked callback rejection:

```csharp
throw new PosCertificateException(
    hasPin ? "POS_CERTIFICATE_CHANGED" : "POS_CERTIFICATE_UNTRUSTED",
    hasPin
        ? "The POS certificate no longer matches the approved fingerprint."
        : "The POS certificate is not trusted. Open Settings to inspect and approve it.",
    exception);
```

Do not infer certificate rejection from exception text.

Add `.RemoveAllLoggers()` between `AddHttpClient<PosHttpClient>()` and the
primary-handler configuration.

- [ ] **Step 4: Run certificate, WPF source, and transport tests**

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~Certificate|FullyQualifiedName~WpfStartupTests|FullyQualifiedName~PosHttpClientTests"
```

- [ ] **Step 5: Commit**

```bash
git add src/RetwhoConnector.Core/Services/PosHttpRequestFactory.cs src/RetwhoConnector.Core/Services/PosHttpClientHandlerFactory.cs src/RetwhoConnector.Core/Services/PosHttpClient.cs src/RetwhoConnector.App/App.xaml.cs tests/RetwhoConnector.Tests/PosHttpClientTests.cs tests/RetwhoConnector.Tests/WpfStartupTests.cs
git commit -m "fix: report POS certificate rejection safely"
```

### Task 5: Documentation and Completion Gates

**Files:**
- Modify: `README.md`
- Modify: `tests/RetwhoConnector.Tests/DocumentationTests.cs`

**Interfaces:**
- Documents: exact `CGIPortal.LoginRequired` refresh, safe diagnostic fields,
  payload exclusions, and certificate remediation.

- [ ] **Step 1: Add a failing README contract test**

Require README text for `CGIPortal.LoginRequired`, exactly one login/retry,
safe XML fault fields, no raw request/response payload logging, and reopening
Settings for certificate changes.

- [ ] **Step 2: Run the documentation test and verify RED**

```bash
/tmp/retwho-dotnet-sdk/dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DocumentationTests"
```

- [ ] **Step 3: Update README and verify GREEN**

Describe the implemented behavior without including any real credential or
cookie. Run the Task 5 filter again.

- [ ] **Step 4: Run complete automated gates**

Run restore, Debug build, Release build, all Release tests, formatting,
tracked-file/generated-log secret scans, and:

```bash
/tmp/retwho-dotnet-sdk/dotnet publish src/RetwhoConnector.App/RetwhoConnector.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true
```

Expected: zero build warnings/errors, all tests pass, format clean, no unsafe
artifacts, and a PE32+ x64 Windows executable.

- [ ] **Step 5: Commit**

```bash
git add README.md tests/RetwhoConnector.Tests/DocumentationTests.cs
git commit -m "docs: explain POS session recovery diagnostics"
```

- [ ] **Step 6: Report external acceptance limits**

Report that Visual Studio/WPF launch, live certificate behavior, live
`CGIPortal.LoginRequired`, DPAPI cookie replacement, and production bridge
acknowledgement remain unverified without Windows and dedicated test
credentials.
