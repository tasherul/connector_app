namespace RetwhoConnector.Tests;

public sealed class DocumentationTests
{
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
    public void WebAppGuide_DocumentsStructuralSocketAndErrorContracts()
    {
        string guide = ReadWebAppGuide();

        Assert.Contains("{ \"ok\": true, \"result\": {} }", guide, StringComparison.Ordinal);
        Assert.Contains(
            "{ \"ok\": false, \"error\": \"ERROR_CODE: Safe description.\" }",
            guide,
            StringComparison.Ordinal);
        Assert.Contains(
            "The result or error property not used is omitted.",
            guide,
            StringComparison.Ordinal);
        Assert.Contains(
            "WebSocket-only (Engine.IO v4, no auto-upgrade)",
            guide,
            StringComparison.Ordinal);
        Assert.Contains(
            "The connector work deadline is 8 seconds.",
            guide,
            StringComparison.Ordinal);
        Assert.Contains(
            "full successful acknowledgement must remain below one MiB",
            guide,
            StringComparison.OrdinalIgnoreCase);

        foreach (string prefix in new[]
                 {
                     "INVALID_ACTION",
                     "UNSUPPORTED_COMMAND",
                     "NOT_REGISTERED",
                     "SETTINGS_MISSING",
                     "SETTINGS_SAVE_FAILED",
                     "POS_AUTH_EXPIRED",
                     "POS_LOGIN_FAILED",
                     "POS_TIMEOUT",
                     "POS_CERTIFICATE_CHANGED",
                     "POS_CERTIFICATE_UNTRUSTED",
                     "POS_HTTP_ERROR",
                     "POS_UNSUPPORTED_CONTENT_ENCODING",
                     "POS_INVALID_XML",
                     "POS_INVALID_RESPONSE",
                     "PAYLOAD_TOO_LARGE",
                     "COMMAND_CANCELLED",
                     "INTERNAL_ERROR",
                 })
        {
            Assert.Contains($"`{prefix}`", guide, StringComparison.Ordinal);
        }
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
    public void WebAppGuide_LabelsEveryDocumentedConnectorToPosRequest()
    {
        string guide = ReadWebAppGuide();

        foreach (string request in new[]
                 {
                     "POST https://POS_HOST/cgi-bin/NAXML?cmd=vdatetime&cookie=FAKE_COOKIE",
                     "POST https://POS_HOST/cgi-bin/NAXML?cmd=vPLUs&cookie=FAKE_COOKIE",
                     "POST https://POS_HOST/cgi-bin/NAXML?cmd=vPLUs&cookie=FAKE_COOKIE",
                     "POST https://POS_HOST/cgi-bin/NAXML?cmd=vrefinteg&dataset=prodCodes,departments,ageValidations,taxRates,blueLaws,fees&cookie=FAKE_COOKIE",
                     "POST https://POS_HOST/cgi-bin/NAXML?cmd=validate&user=FAKE_USER&passwd=REDACTED",
                 })
        {
            Assert.Contains(
                $"Connector request reference — **connector-to-POS only—not browser-callable**:\n\n```text\n{request}\n```",
                guide,
                StringComparison.Ordinal);
        }

        foreach (string limit in new[]
                 {
                     "\"taxRates\": { \"maxRecords\": 10 }",
                     "\"departments\": { \"maxRecords\": 20 }",
                     "\"prodCodes\": { \"maxRecords\": 30 }",
                     "\"ageValidations\": { \"maxRecords\": 8 }",
                     "\"blueLaws\": { \"maxRecords\": 0 }",
                     "\"fees\": { \"maxRecords\": 25, \"maxFeesPerItem\": 3 }",
                 })
        {
            Assert.Contains(limit, guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WebAppGuide_DocumentsTypeScriptDispatchRecoveryAndPagination()
    {
        string guide = ReadWebAppGuide();

        Assert.Contains("executeAgentAction", guide, StringComparison.Ordinal);
        Assert.Contains(
            "new Promise<AgentAck<T>>((resolve, reject) => {",
            guide,
            StringComparison.Ordinal);
        Assert.Contains("socket.timeout(10_000)", guide, StringComparison.Ordinal);
        Assert.Contains("for (let page = 1;", guide, StringComparison.Ordinal);
        Assert.Contains("actionId: crypto.randomUUID(),", guide, StringComparison.Ordinal);
        Assert.Contains("a distinct action ID per page.", guide, StringComparison.Ordinal);
        Assert.Contains(
            "retry that same logical page with its original action ID.",
            guide,
            StringComparison.Ordinal);
        Assert.Contains("one `validate` login", guide, StringComparison.Ordinal);
        Assert.Contains("one retry of the original POS action", guide, StringComparison.Ordinal);
        Assert.Contains("POS_AUTH_EXPIRED", guide, StringComparison.Ordinal);
        Assert.Contains("agent_data_push", guide, StringComparison.Ordinal);
        Assert.Contains("not automatically", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "do not forward `rawXml`, POS origins, cookies, request details, or diagnostics to the browser.",
            guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never log `rawXml`, POS credentials, cookies, full licenses, or internal connector identities.",
            guide,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsDashboardTrayAndAllLogStores()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.StartsWith(
            "# Hybrid Edge Connector Agent",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("## Status dashboard", readme, StringComparison.Ordinal);
        Assert.Contains(
            "## Notification-area behavior",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            @"%LocalAppData%\RetwhoConnector\Data\agent.db",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("newest 1,000", readme, StringComparison.Ordinal);
        Assert.Contains("14 days", readme, StringComparison.Ordinal);
        Assert.Contains("100,000", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_UsesTheTransactionalConfigurationWorkflow()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "**Save & Test Connection**",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "old saved settings remain unchanged",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "first run",
            readme,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_DocumentsSafeSapphireSessionRecoveryDiagnostics()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "CGIPortal.LoginRequired",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "one `validate` login and one retry of the original POS data action",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`faultCode`, `faultString`, and `message`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "does not log raw request or response payloads",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "reopen **Settings**",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsPluBridgeActionParametersAndPagination()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains("`get_plu_page`", readme, StringComparison.Ordinal);
        Assert.Contains("`get_plu`", readme, StringComparison.Ordinal);
        Assert.Contains(
            "`get_referential_integrity`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`pageSize` defaults to `100`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`upcModifier` defaults to `000`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`prodCodes,departments,ageValidations,taxRates,blueLaws,fees`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "exactly one POS page",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsPluResultSafetyAndSingleSessionRefresh()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "`ok: true` and `found: false`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("omit `rawXml`", readme, StringComparison.Ordinal);
        Assert.Contains(
            "one `validate` login and one retry",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "inventory payloads",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "credential-bearing requests",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsModernDashboardAccessibilityAndWindowsAcceptance()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "compact status rail",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "four columns",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "two-by-two",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "keyboard focus",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "high-contrast",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "screenshots",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Windows-only STA smoke tests",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "unverified on non-Windows",
            readme,
            StringComparison.OrdinalIgnoreCase);
    }
}
