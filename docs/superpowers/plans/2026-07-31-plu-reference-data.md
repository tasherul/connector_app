# PLU and Referential Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three bounded Socket.IO actions that read PLU pages, exact PLUs, and fixed Sapphire/Commander referential datasets and return normalized JSON with one-time POS session recovery.

**Architecture:** Keep ConnectorCoordinator as the only bridge-flow owner. Add typed query/result models, explicit request builders, focused secure XML mappers, and three IPosDataService methods; dispatch all four commands through one generic login-once/retry-once helper.

**Tech Stack:** C# 14, .NET 10 Windows, HttpClient HTTP/1.1, LINQ to XML through SecureXml, System.Text.Json, SocketIOClient 4.0.5, xUnit.

## Global Constraints

- Work directly on main; do not create a feature branch or worktree.
- Use only synthetic FAKE_* credentials and inventory values in source/tests.
- Never contact a real POS or connector.retwho.com from automated tests.
- Preserve normal Windows TLS or explicit origin-bound SHA-256 pinning; never bypass certificate validation.
- Preserve the eight-second command deadline, ten-second ACK boundary, one-MiB bridge limit, two-MiB decompressed POS limit, duplicate sharing, and exactly-once ACK.
- get_plu_page fetches one page only; page defaults to 1 and pageSize defaults to 100 with a maximum of 100.
- get_plu requires a 1-32 digit UPC and defaults upcModifier to 000.
- get_referential_integrity always requests prodCodes,departments,ageValidations,taxRates,blueLaws,fees in that order.
- New results are camelCase normalized JSON with nulls omitted and no rawXml.
- Request URIs, bodies, cookies, XML, product values, and referential values never enter diagnostics.
- Every task follows red-green-refactor, runs its focused tests, and commits before the next task.

---

### Task 1: Define product/reference models and action-parameter validation

**Files:**
- Create: src/RetwhoConnector.Core/Models/PosProductModels.cs
- Create: src/RetwhoConnector.Core/Models/PosReferentialModels.cs
- Create: src/RetwhoConnector.Core/Validation/BridgeActionParameterReader.cs
- Modify: tests/RetwhoConnector.Tests/ConnectorContractTests.cs

**Interfaces:**
- Produces: PluPageQuery(int Page, int PageSize)
- Produces: PluLookupQuery(string Upc, string UpcModifier)
- Produces: PluProduct, PluPageResult, PluLookupResult
- Produces: NamedReference, DepartmentReference, ProductCodeReference, ReferenceDefinition, ReferentialIntegrityLimits, ReferentialIntegrityResult
- Produces: BridgeActionParameterReader.ReadPluPage(JsonElement), ReadPluLookup(JsonElement), and ValidateEmpty(JsonElement, string)
- Consumes: BridgeAction.Params and ConnectorJson.Options

- [ ] **Step 1: Write failing validation and JSON-contract tests**

Add tests with these assertions:

    using JsonDocument defaults = JsonDocument.Parse("{}");
    Assert.Equal(new PluPageQuery(1, 100),
        BridgeActionParameterReader.ReadPluPage(defaults.RootElement));

    using JsonDocument supplied =
        JsonDocument.Parse("""{"page":2,"pageSize":25}""");
    Assert.Equal(new PluPageQuery(2, 25),
        BridgeActionParameterReader.ReadPluPage(supplied.RootElement));

    using JsonDocument lookup =
        JsonDocument.Parse("""{"upc":"00000000000001"}""");
    Assert.Equal(new PluLookupQuery("00000000000001", "000"),
        BridgeActionParameterReader.ReadPluLookup(lookup.RootElement));

Add theories rejecting page 0, non-integer page, pageSize 0/101,
missing/empty/non-digit/33-digit UPC, non-three-digit modifier, and unknown
properties. Assert only safe fixed ArgumentException messages. Serialize
synthetic PluPageResult and PluLookupResult values and assert exact camelCase,
identifier strings, numeric decimals, empty arrays, and omitted null product.

- [ ] **Step 2: Run the contract tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~ConnectorContractTests

Expected: compilation fails because the new models and reader do not exist.

- [ ] **Step 3: Add immutable typed models**

Use sealed records with required init-only properties. The product contract is:

    public sealed record PluProduct
    {
        public required string Upc { get; init; }
        public required string UpcModifier { get; init; }
        public required string Description { get; init; }
        public required string DepartmentId { get; init; }
        public IReadOnlyList<string> FeeIds { get; init; } = [];
        public string? ProductCode { get; init; }
        public decimal? Price { get; init; }
        public IReadOnlyList<string> FlagIds { get; init; } = [];
        public IReadOnlyList<string> TaxRateIds { get; init; } = [];
        public IReadOnlyList<string> IdCheckIds { get; init; } = [];
        public decimal? SellUnit { get; init; }
        public decimal? TaxableRebateAmount { get; init; }
        public IReadOnlyList<IndexedCode> GroupCodes { get; init; } = [];
        public decimal? MaxQuantityPerTransaction { get; init; }
    }

Define PluPageResult with Source=NAXML, Command=vPLUs, Page, TotalPages,
RequestedPageSize, ItemCount, Products, and FetchedAtUtc. Define
PluLookupResult with Source, Command, RequestedUpc, RequestedUpcModifier,
Found, nullable Product, and FetchedAtUtc.

Define referential models with string IDs, strict bool IsFuel values,
nullable ProductCode, and ReferenceDefinition containing RecordType,
nullable Id/Name, plus IReadOnlyDictionary<string,string> Fields.

- [ ] **Step 4: Implement strict parameter readers**

Enumerate JsonElement.EnumerateObject(), reject duplicate or unknown names,
require JsonValueKind.Number plus TryGetInt32 for page values, and use generated
regular expressions or direct character checks for digit-only UPC/modifier.
Never include rejected values in exception messages.

- [ ] **Step 5: Run focused tests**

Run the command from Step 2.
Expected: all ConnectorContractTests pass with zero warnings.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.Core/Models/PosProductModels.cs src/RetwhoConnector.Core/Models/PosReferentialModels.cs src/RetwhoConnector.Core/Validation/BridgeActionParameterReader.cs tests/RetwhoConnector.Tests/ConnectorContractTests.cs
    git commit -m "feat: define PLU bridge contracts"

---

### Task 2: Build exact PLU and referential HTTP requests

**Files:**
- Modify: src/RetwhoConnector.Core/Services/PosHttpRequestFactory.cs
- Modify: tests/RetwhoConnector.Tests/PosProtocolTests.cs

**Interfaces:**
- Consumes: PluPageQuery, PluLookupQuery, ConnectorSettings
- Produces: CreatePluPage(settings, cookie, query)
- Produces: CreatePlu(settings, cookie, query)
- Produces: CreateReferentialIntegrity(settings, cookie)

- [ ] **Step 1: Add failing byte-contract tests**

For a FAKE_COOKIE value, assert:

    Assert.Equal(
        "cmd=vPLUs&cookie=FAKE_COOKIE\r\n\r\n" +
        "<domain:PLUSelect xmlns:domain=\"urn:vfi-sapphire:np.domain.2001-07-01\">" +
        "<pageSize>25</pageSize><page>2</page></domain:PLUSelect>",
        await pageRequest.Content!.ReadAsStringAsync());

Assert the single lookup includes source=keyboard, exact leading-zero UPC,
modifier, pageSize 100, and page 1. Assert referential query/body contains
literal commas and no CRLF/XML. For every request assert HTTP/1.1,
text/plain; charset=UTF-8, explicit UTF-8 byte Content-Length, existing
compatibility headers, no Accept header, and safe CommandKey values.

- [ ] **Step 2: Run request tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter "FullyQualifiedName~PosProtocolTests&Name~Request"

Expected: compilation fails because the factory methods do not exist.

- [ ] **Step 3: Refactor the common factory without changing old requests**

Change the internal Create helper to accept separate query and body strings:

    private HttpRequestMessage Create(
        ConnectorSettings settings,
        string command,
        string query,
        string body)

Keep CreateLogin and CreateVdatetime passing the same encoded value for query
and body so existing byte-contract tests remain unchanged.

- [ ] **Step 4: Generate selectors with LINQ to XML**

Build domain-prefixed selectors with XNamespace and XAttribute, serialize with
SaveOptions.DisableFormatting, and join formLine + "\r\n\r\n" + selector.
Do not concatenate socket text into XML. Use a fixed referential dataset
constant and construct its query/body with literal commas; URI-escape only the
cookie.

- [ ] **Step 5: Run request and regression tests**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~PosProtocolTests

Expected: all POS protocol tests pass, including unchanged login/vdatetime.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.Core/Services/PosHttpRequestFactory.cs tests/RetwhoConnector.Tests/PosProtocolTests.cs
    git commit -m "feat: build PLU NAXML requests"

---

### Task 3: Map PLU XML into normalized models

**Files:**
- Create: src/RetwhoConnector.Core/Services/PluXmlMapper.cs
- Create: tests/RetwhoConnector.Tests/Fixtures/plu-page-success.xml
- Create: tests/RetwhoConnector.Tests/Fixtures/plu-empty.xml
- Create: tests/RetwhoConnector.Tests/Fixtures/plu-multiple.xml
- Create: tests/RetwhoConnector.Tests/PosProductMappingTests.cs

**Interfaces:**
- Produces: PluXmlMapper.ParsePage(string, PluPageQuery, DateTimeOffset)
- Produces: PluXmlMapper.ParseLookup(string, PluLookupQuery, DateTimeOffset)
- Consumes: SecureXml.Parse and product models from Task 1

- [ ] **Step 1: Create sanitized fixtures and failing mapper tests**

The success fixture must use the supplied structural shape but only synthetic
values. Include two products so tests cover optional/multiple arrays:

    <domain:PLUs page="2" ofPages="4"
        xmlns:domain="urn:vfi-sapphire:np.domain.2001-07-01">
      <domain:PLU>
        <upc>00000000000001</upc>
        <upcModifier>000</upcModifier>
        <description>FAKE PRODUCT A</description>
        <department>10</department>
        <fees><fee>0</fee></fees>
        <pcode>400</pcode>
        <price>4.67</price>
        <flags><domain:flag sysid="1"/></flags>
        <taxRates><domain:taxRate sysid="2"/></taxRates>
        <idChecks><domain:idCheck sysid="3"/></idChecks>
        <SellUnit>1.000</SellUnit>
        <taxableRebate><amount>0.00</amount></taxableRebate>
        <groupCode index="0">5</groupCode>
        <maxQtyPerTrans>2.00</maxQtyPerTrans>
      </domain:PLU>
    </domain:PLUs>

Assert every model property, page attributes, item count, fetched UTC time,
empty optional arrays, exact lookup found/not-found, and multiple lookup
rejection. Add invalid-root, DTD, invalid decimal, missing required UPC, and
invalid group index cases.

- [ ] **Step 2: Run mapper tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~PosProductMappingTests

Expected: compilation fails because PluXmlMapper does not exist.

- [ ] **Step 3: Implement strict namespace-independent mapping**

Use Name.LocalName for roots/elements. Require PLUs root; parse page/ofPages as
positive integers. Require Upc, UpcModifier, Description, and DepartmentId.
Parse optional decimals with NumberStyles.Number and CultureInfo.InvariantCulture.
Read sysid attributes into ordered arrays. Parse groupCode index strictly.
Wrap structural/value failures in PosResponseException with
POS_INVALID_RESPONSE and safe fixed messages that do not echo XML values.

- [ ] **Step 4: Implement lookup semantics**

Parse through the same product routine. Zero products returns Found=false and
Product=null; one returns Found=true; more than one throws
POS_INVALID_RESPONSE. Preserve requested UPC/modifier separately.

- [ ] **Step 5: Run focused tests**

Run the command from Step 2.
Expected: all PosProductMappingTests pass.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.Core/Services/PluXmlMapper.cs tests/RetwhoConnector.Tests/Fixtures/plu-page-success.xml tests/RetwhoConnector.Tests/Fixtures/plu-empty.xml tests/RetwhoConnector.Tests/Fixtures/plu-multiple.xml tests/RetwhoConnector.Tests/PosProductMappingTests.cs
    git commit -m "feat: map PLU XML results"

---

### Task 4: Map fixed referential-integrity XML

**Files:**
- Create: src/RetwhoConnector.Core/Services/ReferentialIntegrityXmlMapper.cs
- Create: tests/RetwhoConnector.Tests/Fixtures/referential-integrity-success.xml
- Create: tests/RetwhoConnector.Tests/PosReferentialMappingTests.cs

**Interfaces:**
- Produces: ReferentialIntegrityXmlMapper.Parse(string, DateTimeOffset)
- Consumes: SecureXml.Parse and referential models from Task 1

- [ ] **Step 1: Add a sanitized fixture and failing tests**

Include site, fees limits, one tax rate, two departments (one without
prodCode), two product codes, two age validations, an empty blueLaws
container, and one synthetic fee child with scalar attributes. Assert all
known fields, strict bool conversion, limits, empty lists, ReferenceDefinition
normalization, order preservation, and fetchedAtUtc.

Add tests for unexpected root, DTD, missing site, invalid 0/1 IsFuel,
non-numeric limits, duplicate field names, and nested unsupported fee/blue-law
children.

- [ ] **Step 2: Run mapper tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~PosReferentialMappingTests

Expected: compilation fails because the mapper does not exist.

- [ ] **Step 3: Implement typed known datasets**

Require referentialIntegrity root and site. Map taxRate and ageValidation
sysid/name records, department sysid/name/isFuel/optional prodCode, and
prodCode sysid/name/isFuel. Parse maxRecords and maxFeesPerItem into
ReferentialIntegrityLimits with invariant integers and safe errors.

- [ ] **Step 4: Implement bounded extension records**

For direct fee/blue-law children, map local-name to RecordType, sysid/name to
Id/Name, and other direct attributes/scalar children to an ordinal,
case-sensitive dictionary emitted as camelCase JSON. Reject nested complex
children and duplicate normalized keys instead of dropping data.

- [ ] **Step 5: Run focused tests**

Run the command from Step 2.
Expected: all PosReferentialMappingTests pass.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.Core/Services/ReferentialIntegrityXmlMapper.cs tests/RetwhoConnector.Tests/Fixtures/referential-integrity-success.xml tests/RetwhoConnector.Tests/PosReferentialMappingTests.cs
    git commit -m "feat: map POS referential data"

---

### Task 5: Extend the POS data-service boundary and common fault handling

**Files:**
- Modify: src/RetwhoConnector.Core/Abstractions/ConnectorAbstractions.cs
- Modify: src/RetwhoConnector.Core/Services/PosDataService.cs
- Modify: src/RetwhoConnector.App/App.xaml.cs
- Modify: tests/RetwhoConnector.Tests/PosProtocolTests.cs
- Modify: tests/RetwhoConnector.Tests/PosProtocolEdgeTests.cs
- Modify: tests/RetwhoConnector.Tests/PosHttpClientTests.cs
- Modify: tests/RetwhoConnector.Tests/AgentOrchestrationServiceTests.cs
- Modify: tests/RetwhoConnector.Tests/ExecutionAndBridgeTests.cs

**Interfaces:**
- Produces:

    Task<PluPageResult> GetPluPageAsync(
        ConnectorSettings settings, string cookie, PluPageQuery query,
        CancellationToken cancellationToken);

    Task<PluLookupResult> GetPluAsync(
        ConnectorSettings settings, string cookie, PluLookupQuery query,
        CancellationToken cancellationToken);

    Task<ReferentialIntegrityResult> GetReferentialIntegrityAsync(
        ConnectorSettings settings, string cookie,
        CancellationToken cancellationToken);

- Consumes: request factory methods and XML mappers from Tasks 2-4

- [ ] **Step 1: Write failing service tests**

Use fake IPosHttpClient responses to assert each method sends the matching
request, maps a successful fixture, maps HTTP 401/403 to POS_AUTH_EXPIRED,
maps CGIPortal.LoginRequired to POS_AUTH_EXPIRED, retains unrelated faults as
POS_INVALID_RESPONSE, and propagates cancellation/response-limit errors.

In PosHttpClientTests, send each new request through a fake
HttpMessageHandler and recording IAgentLog. Prove diagnostics include only the
safe command/timing/status/size metadata and exclude FAKE_COOKIE, selector XML,
FAKE PRODUCT values, referential values, and full request URIs.

- [ ] **Step 2: Run focused service tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter "FullyQualifiedName~PosProtocolTests|FullyQualifiedName~PosProtocolEdgeTests|FullyQualifiedName~PosHttpClientTests"

Expected: compilation fails until IPosDataService and fakes gain the methods.

- [ ] **Step 3: Extend the interface and all test fakes**

Add the three exact signatures above. Give every fake explicit implementations
that either return a synthetic typed result or throw InvalidOperationException
when that operation is not expected; do not hide missing test setup with
default interface methods.

- [ ] **Step 4: Consolidate SendAndMapAsync**

Refactor PosDataService around:

    private async Task<TResult> SendAndMapAsync<TResult>(
        HttpRequestMessage request,
        Func<string, DateTimeOffset, TResult> map,
        CancellationToken cancellationToken)

The helper validates cookie input before request creation, sends through
IPosHttpClient, handles HTTP status, calls the mapper with
TimeProvider.GetUtcNow(), and converts only mapper POS_INVALID_RESPONSE plus a
recognized authentication fault into POS_AUTH_EXPIRED. Preserve
POS_INVALID_XML and all existing vdatetime behavior.

- [ ] **Step 5: Register mappers and run tests**

Register PluXmlMapper and ReferentialIntegrityXmlMapper as singletons in
App.BuildHost. Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj

Expected: the complete suite passes with zero warnings.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.Core/Abstractions/ConnectorAbstractions.cs src/RetwhoConnector.Core/Services/PosDataService.cs src/RetwhoConnector.App/App.xaml.cs tests/RetwhoConnector.Tests/PosProtocolTests.cs tests/RetwhoConnector.Tests/PosProtocolEdgeTests.cs tests/RetwhoConnector.Tests/PosHttpClientTests.cs tests/RetwhoConnector.Tests/AgentOrchestrationServiceTests.cs tests/RetwhoConnector.Tests/ExecutionAndBridgeTests.cs
    git commit -m "feat: read PLU and referential POS data"

---

### Task 6: Dispatch new bridge commands with one shared session refresh

**Files:**
- Modify: src/RetwhoConnector.Core/Services/ConnectorCoordinator.cs
- Modify: tests/RetwhoConnector.Tests/ExecutionAndBridgeTests.cs
- Modify: tests/RetwhoConnector.Tests/ConnectorContractTests.cs

**Interfaces:**
- Consumes: BridgeActionParameterReader and IPosDataService methods
- Produces: normalized BridgeAcknowledgement results for all four commands
- Preserves: ResultReceived only for VdatetimeResult

- [ ] **Step 1: Write failing coordinator tests**

Add one test per new command using JsonDocument params. Assert the correct fake
method and arguments, exact normalized result type, no vdatetime call, and one
ACK. Add invalid/default parameter cases, unknown command, one-MiB serialized
payload rejection, duplicate action sharing, and caller cancellation.

For each new operation, add an expired-cookie test that asserts:

    Assert.Equal(1, authentication.Calls);
    Assert.Equal(2, data.OperationCalls);
    Assert.Equal("FAKE_NEW_COOKIE", settingsService.Settings!.PosCookie);

Add a second-expiry theory for all commands asserting one login, two data
calls, and POS_AUTH_EXPIRED.

- [ ] **Step 2: Run coordinator tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~ExecutionAndBridgeTests

Expected: new actions return UNSUPPORTED_COMMAND.

- [ ] **Step 3: Add typed dispatch**

Replace the get_current_data-only branch with a switch expression that first
reads/validates parameters and returns a delegate:

    Func<ConnectorSettings, string, CancellationToken, Task<object>> operation

Each delegate calls exactly one typed data-service method and boxes its result.
Keep unsupported commands returning the existing safe code. Validate before
waiting on the POS semaphore or loading credentials.

- [ ] **Step 4: Generalize session refresh**

Implement:

    private async Task<T> GetWithOneRefreshAsync<T>(
        ConnectorSettings settings,
        Func<ConnectorSettings, string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)

Use the saved cookie once; on PosAuthenticationException call validate once,
save the encrypted replacement cookie, and call operation once more. Never
catch the second PosAuthenticationException. Fire ResultReceived only when the
returned object is VdatetimeResult.

- [ ] **Step 5: Verify bridge contracts**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter "FullyQualifiedName~ExecutionAndBridgeTests|FullyQualifiedName~ConnectorContractTests"

Expected: all command, duplicate, payload, timeout, and exactly-once tests pass.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.Core/Services/ConnectorCoordinator.cs tests/RetwhoConnector.Tests/ExecutionAndBridgeTests.cs tests/RetwhoConnector.Tests/ConnectorContractTests.cs
    git commit -m "feat: execute PLU bridge commands"

---

### Task 7: Document and verify the data feature

**Files:**
- Modify: README.md
- Modify: tests/RetwhoConnector.Tests/DocumentationTests.cs

**Interfaces:**
- Documents: action names, parameters/defaults, normalized results, pagination,
  fixed datasets, one-refresh semantics, limits, and safe logging

- [ ] **Step 1: Add failing documentation checks**

Require README to contain get_plu_page, get_plu,
get_referential_integrity, pageSize 100, upcModifier 000, the fixed dataset
list, one-page-per-action behavior, and one-login/one-retry behavior.

- [ ] **Step 2: Run documentation tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~DocumentationTests

Expected: missing new command documentation fails.

- [ ] **Step 3: Update README**

Add request/response examples with synthetic values only. Explain that the
cloud owns pagination, get_plu not-found is ok=true/found=false, new results
omit rawXml, and logs never contain inventory payloads or credential-bearing
requests.

- [ ] **Step 4: Run all automated gates**

Run:

    dotnet restore RetwhoConnector.sln
    dotnet build RetwhoConnector.sln -c Debug --no-restore
    dotnet build RetwhoConnector.sln -c Release --no-restore
    dotnet test RetwhoConnector.sln -c Release --no-build
    dotnet format RetwhoConnector.sln --verify-no-changes --no-restore

Expected: zero warnings, all tests pass, formatting clean.

- [ ] **Step 5: Scan for unsafe content**

Run:

    git grep -nE '3ba5d0[8]1|cookie=[0-9a-fA-F]{16,}|passw[d]=|NotImplementedExceptio[n]|T[O]DO|T[B]D'
    git status --short

Expected: no real cookie/credential, placeholder, generated artifact, or
unintended change. Any deliberate documentation mention uses FAKE_* only.

- [ ] **Step 6: Commit**

    git add README.md tests/RetwhoConnector.Tests/DocumentationTests.cs
    git commit -m "docs: document PLU bridge operations"

Plan 1 is complete only when every commit is on main and all automated gates
pass. Live POS/bridge acceptance remains unverified without dedicated test
credentials.
