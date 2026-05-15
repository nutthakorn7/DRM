# Phase 5K Generic File Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the desktop protection client protect arbitrary file types into the existing `.drmx` encrypted container.

**Architecture:** Add a generic `ProtectFileWorkflow` in agent core that owns the file-based protection sequence: register metadata, wrap the file key, write a protected container, verify the header, optionally save the local fallback key, update inventory, and optionally delete the original. Keep `ProtectPdfFileWorkflow` as a compatibility wrapper that still rejects non-PDF paths and delegates to the generic workflow with `application/pdf`. Update the tray UI labels, picker, and workflow call so desktop users can protect any file while still using policy templates and recipients.

**Tech Stack:** .NET 10, WPF, existing DRM container format, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/ProtectFileWorkflow.cs`: generic file protection workflow, result/options records, content type inference.
- Modify `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs`: delegate PDF-specific behavior to `ProtectFileWorkflow`.
- Modify `tests/Drm.Agent.Core.Tests/ProtectPdfFileWorkflowTests.cs`: add generic workflow tests and keep PDF wrapper compatibility coverage.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml`: relabel PDF-specific UI to generic file protection.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`: use `ProtectFileWorkflow` and allow the file picker to select any file.
- Modify `README.md`: document Phase 5K generic file protection.

## Tasks

### Task 1: Agent Core Generic File Protection

- [x] **Step 1: Write failing generic workflow tests**

Add tests to `tests/Drm.Agent.Core.Tests/ProtectPdfFileWorkflowTests.cs`:

```csharp
[Fact]
public async Task ProtectFileWorkflow_protects_non_pdf_file_with_inferred_content_type()
{
    var tempDirectory = Directory.CreateTempSubdirectory();
    var sourcePath = Path.Combine(tempDirectory.FullName, "contract.docx");
    await File.WriteAllBytesAsync(sourcePath, "office bytes"u8.ToArray());
    var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
    var tenantId = TenantId.New();
    var ownerUserId = UserId.New();
    var fileKey = EnvelopeCrypto.GenerateKey();
    var server = new RecordingServerClient();

    var result = await new ProtectFileWorkflow(server, inventory)
        .ProtectAsync(
            tenantId,
            ownerUserId,
            sourcePath,
            fileKey,
            ProtectFilePolicyOptions.Default,
            deleteOriginalAfterProtection: false,
            CancellationToken.None);

    result.DestinationPath.Should().Be($"{sourcePath}.drmx");
    result.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    File.Exists(sourcePath).Should().BeTrue();
    File.Exists(result.DestinationPath).Should().BeTrue();

    await using var output = File.OpenRead(result.DestinationPath);
    var package = ProtectedFileReader.Read(output);
    package.Header.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    package.Decrypt(fileKey).Should().Equal("office bytes"u8.ToArray());
    server.RegisteredFileRequests.Should().ContainSingle(request =>
        request.ContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" &&
        request.FileId == result.FileId);
}

[Fact]
public async Task ProtectFileWorkflow_defaults_unknown_extension_to_octet_stream()
{
    var tempDirectory = Directory.CreateTempSubdirectory();
    var sourcePath = Path.Combine(tempDirectory.FullName, "model.cadbin");
    await File.WriteAllBytesAsync(sourcePath, "cad bytes"u8.ToArray());
    var inventory = new JsonProtectedFileInventory(Path.Combine(tempDirectory.FullName, "inventory.json"));
    var server = new RecordingServerClient();

    var result = await new ProtectFileWorkflow(server, inventory)
        .ProtectAsync(
            TenantId.New(),
            UserId.New(),
            sourcePath,
            EnvelopeCrypto.GenerateKey(),
            ProtectFilePolicyOptions.Default,
            deleteOriginalAfterProtection: false,
            CancellationToken.None);

    result.ContentType.Should().Be("application/octet-stream");
    server.RegisteredFileRequests.Should().ContainSingle(request =>
        request.ContentType == "application/octet-stream");
}
```

- [x] **Step 2: Run failing generic workflow tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "ProtectFileWorkflow_protects_non_pdf_file_with_inferred_content_type|ProtectFileWorkflow_defaults_unknown_extension_to_octet_stream"
```

Expected: FAIL because `ProtectFileWorkflow` and `ProtectFilePolicyOptions` do not exist.

- [x] **Step 3: Implement generic workflow**

Create `src/Drm.Agent.Core/ProtectFileWorkflow.cs` with:

```csharp
public sealed class ProtectFileWorkflow(
    IDrmServerClient serverClient,
    IProtectedFileInventory inventory,
    IFileKeyStore? fileKeyStore = null)
{
    public Task<ProtectedFileResult> ProtectAsync(
        TenantId tenantId,
        UserId ownerUserId,
        string sourcePath,
        byte[] fileKey,
        ProtectFilePolicyOptions policyOptions,
        bool deleteOriginalAfterProtection,
        CancellationToken cancellationToken);
}

public sealed record ProtectedFileResult(
    Guid TenantId,
    Guid FileId,
    string SourcePath,
    string DestinationPath,
    string ContentType,
    bool OriginalDeleted);

public sealed record ProtectFilePolicyOptions(
    Permission Permissions,
    Guid? PolicyTemplateId,
    IReadOnlyList<ProtectionRecipient> Recipients)
{
    public static ProtectFilePolicyOptions Default { get; } = new(
        Permission.View | Permission.Print,
        PolicyTemplateId: null,
        Recipients: []);
}
```

The implementation must:

- validate `sourcePath`, `fileKey`, and `policyOptions`;
- reject missing source files with `FileNotFoundException("Source file was not found.", sourcePath)`;
- infer content type from common extensions: `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.ppt`, `.pptx`, `.zip`, `.dwg`, `.dxf`, `.txt`, `.csv`, and otherwise `application/octet-stream`;
- call `RegisterFileAsync(ProtectedFileRegistration, ...)` with the inferred content type;
- call `WrapFileKeyAsync` before writing committed output;
- write to `<source>.drmx.<guid>.tmp`, verify the protected header tenant/file/content type, and move to `<source>.drmx`;
- save the local key and inventory after the protected output is committed;
- delete the source only after all prior steps succeed;
- delete leftover temp output in `finally`.

- [x] **Step 4: Run passing generic workflow tests**

Run the same filtered test command. Expected: PASS.

### Task 2: PDF Wrapper Compatibility

- [x] **Step 1: Write/update PDF wrapper tests**

Add or keep assertions that `ProtectPdfFileWorkflow` still:

- rejects non-PDF files with `Only PDF files can be protected by this workflow.`;
- returns `ProtectedPdfFileResult`;
- writes a PDF container with `application/pdf`;
- passes policy template and recipients through to the server.

- [x] **Step 2: Delegate PDF workflow to generic workflow**

Update `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs` so it checks `.pdf`, converts `ProtectPdfPolicyOptions` into `ProtectFilePolicyOptions`, calls `ProtectFileWorkflow.ProtectAsync`, and maps `ProtectedFileResult` back to `ProtectedPdfFileResult`.

- [x] **Step 3: Run PDF workflow tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "ProtectPdfFileWorkflow"
```

Expected: PASS.

### Task 3: Tray Generic Protection UI

- [x] **Step 1: Update tray UI labels**

Change `src/Drm.Agent.Tray.Windows/MainWindow.xaml`:

- subtitle: `Protect files into managed encrypted containers.`
- file label: `Source file`
- delete checkbox: `Delete original file after successful protection`
- button text: `Protect file`

- [x] **Step 2: Wire tray to generic workflow**

Update `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`:

- file picker filter: `All supported files (*.*)|*.*`
- dialog title: `Select file to protect`
- progress status: `Protecting file...`
- missing source message: `Select a file before protecting.`
- instantiate `ProtectFileWorkflow`
- call `new ProtectFilePolicyOptions(Permission.View | Permission.Print, policyTemplateId, recipients)`

- [x] **Step 3: Update README**

Add Phase 5K notes that the tray can protect arbitrary files, stores the original content type in the `.drmx` header, and still uses the same policy template/recipient path.

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
git add README.md src/Drm.Agent.Core src/Drm.Agent.Tray.Windows tests/Drm.Agent.Core.Tests docs/superpowers/plans/2026-05-15-phase-5k-generic-file-protection.md
git commit -m "feat: protect generic desktop files"
```

## Self-Review

- Spec coverage: Implements the broader-file-support roadmap's generic encrypted container path without claiming native Office/CAD rendering yet.
- Security note: This still relies on the existing MVP desktop identity/client-key model; production endpoint authentication and local secret protection remain future hardening work.
- Placeholder scan: No TBD/TODO placeholders.
