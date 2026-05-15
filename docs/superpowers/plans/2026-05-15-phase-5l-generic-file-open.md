# Phase 5L Generic File Open Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the desktop open path decrypt protected non-PDF files while preserving PDF viewer compatibility.

**Architecture:** Introduce `OpenProtectedFileWorkflow` as the generic core open workflow for both byte-array and file-path entry points. It returns `OpenedProtectedFile` with `ContentType` from the protected-file header, while `OpenProtectedPdfWorkflow` and `OpenProtectedPdfFileWorkflow` become compatibility wrappers that still return `OpenedProtectedPdf`. Update the Windows viewer so PDF payloads still render inline, and non-PDF payloads load as protected generic files with export gated by policy.

**Tech Stack:** .NET 10, existing DRM container format, WPF viewer, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/OpenProtectedFileWorkflow.cs`: generic open workflow, path/key unwrap flow, byte-array policy decision flow, result record.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs`: delegate byte-array PDF behavior to `OpenProtectedFileWorkflow`.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfFileWorkflow.cs`: delegate path-based PDF behavior to `OpenProtectedFileWorkflow`.
- Modify `tests/Drm.Agent.Core.Tests/OpenProtectedPdfFileWorkflowTests.cs`: add generic non-PDF open tests and keep existing wrapper tests passing.
- Modify `tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs`: add byte-array generic open coverage for content type.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml`: change viewer labels from PDF-specific to protected-file wording.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs`: use generic open result, render only PDFs inline, export non-PDF files with content-type-derived extension.
- Modify `README.md`: document Phase 5L.

## Tasks

### Task 1: Agent Core Generic Open Workflow

- [x] **Step 1: Write failing generic file-open tests**

Add tests:

```csharp
[Fact]
public async Task OpenProtectedFileWorkflow_opens_non_pdf_file_and_returns_content_type()
{
    var tempDirectory = Directory.CreateTempSubdirectory();
    var sourcePath = Path.Combine(tempDirectory.FullName, "contract.docx");
    var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
    var keyStore = new JsonFileKeyStore(Path.Combine(tempDirectory.FullName, "keys.json"));
    var server = new AllowingServerClient();
    var tenantId = TenantId.New();
    var userId = UserId.New();
    var deviceId = DeviceId.New();
    await File.WriteAllBytesAsync(sourcePath, "office bytes"u8.ToArray());

    var protectedFile = await new ProtectFileWorkflow(server, inventory, keyStore)
        .ProtectAsync(
            tenantId,
            userId,
            sourcePath,
            EnvelopeCrypto.GenerateKey(),
            ProtectFilePolicyOptions.Default,
            deleteOriginalAfterProtection: false,
            CancellationToken.None);

    var opened = await new OpenProtectedFileWorkflow(server, keyStore)
        .OpenAsync(protectedFile.DestinationPath, userId, deviceId, CancellationToken.None);

    opened.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    opened.Content.Should().Equal("office bytes"u8.ToArray());
    opened.TenantId.Should().Be(tenantId.Value);
    opened.FileId.Should().Be(protectedFile.FileId);
}
```

```csharp
[Fact]
public async Task OpenProtectedFileWorkflow_byte_array_open_returns_header_content_type()
{
    var server = new FakeDrmServerClient();
    var tenantId = TenantId.New();
    var userId = UserId.New();
    var deviceId = DeviceId.New();
    var fileId = ProtectedFileId.New();
    var fileKey = EnvelopeCrypto.GenerateKey();
    using var output = new MemoryStream();
    ProtectedFileWriter.Write(
        output,
        tenantId,
        fileId,
        "text/csv",
        fileKey,
        "a,b"u8.ToArray());

    await server.RegisterFileAsync(
        tenantId.Value,
        fileId.Value,
        userId.Value,
        "text/csv",
        DateTimeOffset.UtcNow.AddHours(1),
        Permission.View,
        CancellationToken.None);

    var opened = await new OpenProtectedFileWorkflow(server)
        .OpenAsync(output.ToArray(), userId, deviceId, fileKey, CancellationToken.None);

    opened.ContentType.Should().Be("text/csv");
    opened.Content.Should().Equal("a,b"u8.ToArray());
}
```

- [x] **Step 2: Run failing generic open tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "OpenProtectedFileWorkflow_opens_non_pdf_file_and_returns_content_type|OpenProtectedFileWorkflow_byte_array_open_returns_header_content_type"
```

Expected: FAIL because `OpenProtectedFileWorkflow` and `OpenedProtectedFile` do not exist.

- [x] **Step 3: Implement generic open workflow**

Create `src/Drm.Agent.Core/OpenProtectedFileWorkflow.cs` with:

```csharp
public sealed class OpenProtectedFileWorkflow(
    IDrmServerClient serverClient,
    IFileKeyStore? fileKeyStore = null,
    IPolicyDecisionCache? decisionCache = null)
{
    public Task<OpenedProtectedFile> OpenAsync(
        byte[] protectedBytes,
        UserId userId,
        DeviceId deviceId,
        byte[] fileKey,
        CancellationToken cancellationToken);

    public Task<OpenedProtectedFile> OpenAsync(
        string protectedPath,
        UserId userId,
        DeviceId deviceId,
        CancellationToken cancellationToken);
}

public sealed record OpenedProtectedFile(
    Guid TenantId,
    Guid FileId,
    string ContentType,
    byte[] Content,
    string Watermark,
    Permission Permissions);
```

Move the existing unwrap, local-key fallback, decision-cache, watermark, and policy-decision logic from the PDF workflows into this generic class. `OpenWithDecision` must populate `ContentType` from `package.Header.ContentType`.

- [x] **Step 4: Run passing generic open tests**

Run the same filtered command. Expected: PASS.

### Task 2: PDF Compatibility Wrappers

- [x] **Step 1: Delegate PDF workflows**

Update:

- `OpenProtectedPdfWorkflow.OpenAsync(byte[]...)` to call `new OpenProtectedFileWorkflow(serverClient, decisionCache: decisionCache).OpenAsync(...)`.
- `OpenProtectedPdfFileWorkflow.OpenAsync(string...)` to call `new OpenProtectedFileWorkflow(serverClient, fileKeyStore, decisionCache).OpenAsync(...)`.
- Map `OpenedProtectedFile` to `OpenedProtectedPdf`.

Do not remove `OpenedProtectedPdf`; existing callers keep compiling.

- [x] **Step 2: Run PDF open tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "OpenProtectedPdfFileWorkflow|ProtectAndOpenWorkflowTests"
```

Expected: PASS.

### Task 3: Viewer Generic File Handling

- [x] **Step 1: Update viewer labels**

Change `src/Drm.Viewer.Windows/MainWindow.xaml`:

- toolbar label from `Protected PDF` to `Protected file`;
- status strings in code from `Loaded protected PDF` and `Export original PDF` to generic `file` wording.

- [x] **Step 2: Use generic open result**

Update `src/Drm.Viewer.Windows/MainWindow.xaml.cs` so `OpenButton_Click` uses `OpenProtectedFileWorkflow`.

Behavior:

- if `ContentType == "application/pdf"`, write a temp `.pdf`, navigate the embedded browser, and keep Copy/Print/Export behavior;
- otherwise, navigate the browser to `about:blank`, keep content in memory, display status with the content type, and enable only export when `ExportOriginal` is allowed.

- [x] **Step 3: Export with content-type extension**

Add a private mapping for export extensions:

- PDF `.pdf`
- DOCX `.docx`
- XLSX `.xlsx`
- PPTX `.pptx`
- ZIP `.zip`
- text `.txt`
- CSV `.csv`
- default `.bin`

When the protected file is `contract.docx.drmx`, the default export name should be `contract.docx`, not `contract.docx.docx`.

- [x] **Step 4: Update README**

Add Phase 5L notes for generic open/decrypt/export and the current limit that inline rendering is still PDF-only.

### Task 4: Verification and Commit

- [x] **Step 1: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

Expected: all pass.

- [x] **Step 2: Commit**

Run:

```bash
git add README.md src/Drm.Agent.Core src/Drm.Viewer.Windows tests/Drm.Agent.Core.Tests docs/superpowers/plans/2026-05-15-phase-5l-generic-file-open.md
git commit -m "feat: open generic protected files"
```

## Self-Review

- Spec coverage: Completes the generic container loop by supporting protect plus open/decrypt/export for non-PDF payloads.
- Security note: Non-PDF export still requires `ExportOriginal`; inline rendering and native app control for Office/CAD are future endpoint-control work.
- Placeholder scan: No TBD/TODO placeholders.
