# Phase 5B Desktop Client API Key Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let desktop clients send `X-DRM-Client-Key` to servers that enabled Phase 5A client API key auth.

**Architecture:** Add optional client-key support to `DrmServerClient` by setting a default request header when configured. Wire Windows service configuration through `DrmAgent:ClientApiKey`. Add Client API key fields to the tray protector and viewer so manual desktop workflows can connect to protected servers.

**Tech Stack:** .NET 10, HttpClient, WPF, xUnit, FluentAssertions.

---

## File Structure

- Modify `tests/Drm.Agent.Core.Tests/AgentClientTests.cs`: assert configured client key is sent as `X-DRM-Client-Key`.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: add optional constructor parameter and header constant.
- Modify `src/Drm.Agent.Service.Windows/AgentServiceOptions.cs`: add `ClientApiKey`.
- Modify `src/Drm.Agent.Service.Windows/Program.cs`: pass configured key to `DrmServerClient`.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml` and `.xaml.cs`: add Client API key field and header wiring.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml` and `.xaml.cs`: add Client API key field and header wiring.
- Modify `README.md`: document desktop client key support.

## Tasks

### Task 1: Core Client Header

- [x] **Step 1: Write failing core client test**

Add a test in `tests/Drm.Agent.Core.Tests/AgentClientTests.cs`:

```csharp
[Fact]
public async Task DrmServerClient_sends_client_api_key_header_when_configured()
{
    HttpRequestMessage? capturedRequest = null;
    var handler = new StubHttpMessageHandler(request =>
    {
        capturedRequest = request;
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    });

    var client = new DrmServerClient(new HttpClient(handler)
    {
        BaseAddress = new Uri("https://drm.example")
    }, "client-key");
    var record = new AgentAuditRecord(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "agent_heartbeat",
        "online",
        DateTimeOffset.UtcNow);

    await client.UploadAuditAsync(record, CancellationToken.None);

    capturedRequest.Should().NotBeNull();
    capturedRequest!.Headers.GetValues(DrmServerClient.ClientApiKeyHeaderName)
        .Should()
        .ContainSingle()
        .Which
        .Should()
        .Be("client-key");
}
```

- [x] **Step 2: Run failing core client test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter DrmServerClient_sends_client_api_key_header_when_configured
```

Expected: FAIL to compile because `DrmServerClient` does not yet expose a client-key constructor or header constant.

- [x] **Step 3: Implement `DrmServerClient` key support**

Convert `DrmServerClient` from a primary-constructor class to a normal class with:

```csharp
public const string ClientApiKeyHeaderName = "X-DRM-Client-Key";

private readonly HttpClient httpClient;

public DrmServerClient(HttpClient httpClient, string? clientApiKey = null)
{
    this.httpClient = httpClient;
    if (!string.IsNullOrWhiteSpace(clientApiKey))
    {
        httpClient.DefaultRequestHeaders.Remove(ClientApiKeyHeaderName);
        httpClient.DefaultRequestHeaders.Add(ClientApiKeyHeaderName, clientApiKey.Trim());
    }
}
```

- [x] **Step 4: Run passing core client test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter DrmServerClient_sends_client_api_key_header_when_configured
```

Expected: PASS.

### Task 2: Desktop Wiring

- [x] **Step 1: Wire Windows service config**

Add `public string? ClientApiKey { get; set; }` to `AgentServiceOptions`. In `Program.cs`, construct the typed client with:

```csharp
builder.Services.AddHttpClient<IDrmServerClient, DrmServerClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
    if (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var serverUri))
    {
        throw new InvalidOperationException("DrmAgent:ServerUrl must be an absolute URI.");
    }

    client.BaseAddress = serverUri;
})
.AddTypedClient((httpClient, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
    return new DrmServerClient(httpClient, options.ClientApiKey);
});
```

- [x] **Step 2: Wire tray client key field**

Add a `PasswordBox x:Name="ClientApiKeyBox"` row to `src/Drm.Agent.Tray.Windows/MainWindow.xaml`. In `MainWindow.xaml.cs`, before constructing `DrmServerClient`, add:

```csharp
var clientApiKey = ClientApiKeyBox.Password.Trim();
using var httpClient = new HttpClient { BaseAddress = serverUrl };
var serverClient = new DrmServerClient(httpClient, clientApiKey);
```

- [x] **Step 3: Wire viewer client key field**

Add a `PasswordBox x:Name="ClientApiKeyBox"` row to `src/Drm.Viewer.Windows/MainWindow.xaml`. In `MainWindow.xaml.cs`, construct:

```csharp
var clientApiKey = ClientApiKeyBox.Password.Trim();
using var httpClient = new HttpClient { BaseAddress = serverUrl };
var serverClient = new DrmServerClient(httpClient, clientApiKey);
```

- [x] **Step 4: Run targeted builds**

Run:

```bash
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add:

```markdown
## Phase 5B Desktop Client API Key

Desktop clients can now send `X-DRM-Client-Key`. The Windows service reads `DrmAgent:ClientApiKey`, and the tray protector/viewer include Client API key fields for manual workflows.
```

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 3: Commit**

Run:

```bash
git add README.md src/Drm.Agent.Core src/Drm.Agent.Service.Windows src/Drm.Agent.Tray.Windows src/Drm.Viewer.Windows tests/Drm.Agent.Core.Tests docs/superpowers/plans/2026-05-15-phase-5b-client-api-key-desktop.md
git commit -m "feat: send client api key from desktop clients"
```

## Self-Review

- Spec coverage: Covers core HTTP client, Windows service, tray protector, and viewer.
- Compatibility note: Blank client key keeps existing headerless behavior.
- Placeholder scan: No TBD/TODO placeholders.
