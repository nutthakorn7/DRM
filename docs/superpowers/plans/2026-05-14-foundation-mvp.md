# Enterprise DRM Foundation MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first working vertical slice of the enterprise DRM platform: protect a PDF into an encrypted container, register it with a management server, open it through a Windows client/viewer after a policy decision, audit the access, and revoke future access.

**Architecture:** Use one .NET 10 codebase with focused projects for domain policy, cryptography, protected containers, server APIs, and Windows agent/viewer components. The server supports both SaaS and on-prem modes through configuration, while the Windows client connects to a configurable server URL and keeps enforcement visible through a service plus tray/viewer app.

**Tech Stack:** .NET 10 LTS, ASP.NET Core Minimal APIs, EF Core with PostgreSQL for real deployments and SQLite for tests/local smoke runs, WPF + WebView2 for the Windows viewer shell, xUnit, FluentAssertions, Testcontainers where Docker is available.

---

## Scope Boundary

This plan implements Phase 1 from the approved design spec only. It does not implement Office/CAD support, transparent folder encryption, Outlook add-in, browser viewer, SAML/SCIM, advanced screenshot deterrence, app allow/block lists, or production KMS/HSM integrations. Those require separate plans after this foundation is stable.

The repository is currently empty except for the approved design document. Every task below starts from that state.

## File Structure

Create this structure:

```text
Directory.Build.props
README.md
docs/superpowers/specs/2026-05-14-enterprise-drm-design.md
docs/superpowers/plans/2026-05-14-foundation-mvp.md
src/Drm.Domain/Drm.Domain.csproj
src/Drm.Domain/Ids.cs
src/Drm.Domain/Permissions.cs
src/Drm.Domain/Policy.cs
src/Drm.Domain/PolicyDecision.cs
src/Drm.Domain/PolicyEvaluator.cs
src/Drm.Crypto/Drm.Crypto.csproj
src/Drm.Crypto/AesGcmPayload.cs
src/Drm.Crypto/EnvelopeCrypto.cs
src/Drm.Container/Drm.Container.csproj
src/Drm.Container/ProtectedFileHeader.cs
src/Drm.Container/ProtectedFilePackage.cs
src/Drm.Container/ProtectedFileWriter.cs
src/Drm.Container/ProtectedFileReader.cs
src/Drm.Server/Drm.Server.csproj
src/Drm.Server/Program.cs
src/Drm.Server/AppDbContext.cs
src/Drm.Server/Entities.cs
src/Drm.Server/ServerMode.cs
src/Drm.Server/SeedData.cs
src/Drm.Server/Endpoints/FilesEndpoints.cs
src/Drm.Server/Endpoints/PolicyEndpoints.cs
src/Drm.Server/Endpoints/AuditEndpoints.cs
src/Drm.Agent.Core/Drm.Agent.Core.csproj
src/Drm.Agent.Core/DrmServerClient.cs
src/Drm.Agent.Core/ProtectPdfWorkflow.cs
src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs
src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
src/Drm.Agent.Service.Windows/Program.cs
src/Drm.Agent.Service.Windows/Worker.cs
src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
src/Drm.Agent.Tray.Windows/App.xaml
src/Drm.Agent.Tray.Windows/App.xaml.cs
src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
src/Drm.Viewer.Windows/App.xaml
src/Drm.Viewer.Windows/App.xaml.cs
src/Drm.Viewer.Windows/MainWindow.xaml
src/Drm.Viewer.Windows/MainWindow.xaml.cs
tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
tests/Drm.Domain.Tests/PolicyEvaluatorTests.cs
tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
tests/Drm.Crypto.Tests/EnvelopeCryptoTests.cs
tests/Drm.Container.Tests/Drm.Container.Tests.csproj
tests/Drm.Container.Tests/ProtectedFilePackageTests.cs
tests/Drm.Server.Tests/Drm.Server.Tests.csproj
tests/Drm.Server.Tests/PolicyApiTests.cs
tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs
tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj
tests/Drm.Integration.Tests/ServerIntegratedWorkflowTests.cs
```

Boundaries:

- `Drm.Domain` owns pure business rules and must not reference ASP.NET, EF Core, WPF, WebView2, or file I/O.
- `Drm.Crypto` owns authenticated encryption and key wrapping helpers.
- `Drm.Container` owns the encrypted file format and uses `Drm.Crypto`.
- `Drm.Server` owns persistence, APIs, deployment mode configuration, and audit ingestion.
- `Drm.Agent.Core` owns client workflows that are testable without Windows UI.
- `Drm.Agent.Service.Windows`, `Drm.Agent.Tray.Windows`, and `Drm.Viewer.Windows` contain Windows-specific shells only.

## Task 1: Scaffold Solution and Shared Build Settings

**Files:**
- Create: `Directory.Build.props`
- Create: `README.md`
- Create: all `.csproj` files listed above except `tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj`, which is created in Task 7

- [ ] **Step 1: Create the solution and projects**

Run:

```bash
dotnet new sln -n Drm
dotnet new classlib -n Drm.Domain -o src/Drm.Domain
dotnet new classlib -n Drm.Crypto -o src/Drm.Crypto
dotnet new classlib -n Drm.Container -o src/Drm.Container
dotnet new web -n Drm.Server -o src/Drm.Server
dotnet new classlib -n Drm.Agent.Core -o src/Drm.Agent.Core
dotnet new worker -n Drm.Agent.Service.Windows -o src/Drm.Agent.Service.Windows
dotnet new wpf -n Drm.Agent.Tray.Windows -o src/Drm.Agent.Tray.Windows
dotnet new wpf -n Drm.Viewer.Windows -o src/Drm.Viewer.Windows
dotnet new xunit -n Drm.Domain.Tests -o tests/Drm.Domain.Tests
dotnet new xunit -n Drm.Crypto.Tests -o tests/Drm.Crypto.Tests
dotnet new xunit -n Drm.Container.Tests -o tests/Drm.Container.Tests
dotnet new xunit -n Drm.Server.Tests -o tests/Drm.Server.Tests
dotnet new xunit -n Drm.Agent.Core.Tests -o tests/Drm.Agent.Core.Tests
dotnet sln add src/*/*.csproj tests/*/*.csproj
```

Expected: all projects are created and added to `Drm.sln`.

- [ ] **Step 2: Add shared build settings**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Fix Windows project target frameworks**

Set these project files to Windows-only target frameworks:

```xml
<!-- src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsService>true</UseWindowsService>
  </PropertyGroup>
</Project>
```

```xml
<!-- src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

```xml
<!-- src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Add project references and packages**

Run:

```bash
dotnet add src/Drm.Crypto/Drm.Crypto.csproj reference src/Drm.Domain/Drm.Domain.csproj
dotnet add src/Drm.Container/Drm.Container.csproj reference src/Drm.Domain/Drm.Domain.csproj src/Drm.Crypto/Drm.Crypto.csproj
dotnet add src/Drm.Server/Drm.Server.csproj reference src/Drm.Domain/Drm.Domain.csproj src/Drm.Crypto/Drm.Crypto.csproj src/Drm.Container/Drm.Container.csproj
dotnet add src/Drm.Agent.Core/Drm.Agent.Core.csproj reference src/Drm.Domain/Drm.Domain.csproj src/Drm.Crypto/Drm.Crypto.csproj src/Drm.Container/Drm.Container.csproj
dotnet add src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj reference src/Drm.Agent.Core/Drm.Agent.Core.csproj
dotnet add src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj reference src/Drm.Agent.Core/Drm.Agent.Core.csproj
dotnet add src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj reference src/Drm.Agent.Core/Drm.Agent.Core.csproj
dotnet add src/Drm.Server/Drm.Server.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/Drm.Server/Drm.Server.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj package Microsoft.Web.WebView2
dotnet add tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj reference src/Drm.Domain/Drm.Domain.csproj
dotnet add tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj reference src/Drm.Crypto/Drm.Crypto.csproj
dotnet add tests/Drm.Container.Tests/Drm.Container.Tests.csproj reference src/Drm.Container/Drm.Container.csproj
dotnet add tests/Drm.Server.Tests/Drm.Server.Tests.csproj reference src/Drm.Server/Drm.Server.csproj
dotnet add tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj reference src/Drm.Agent.Core/Drm.Agent.Core.csproj
dotnet add tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj package FluentAssertions
dotnet add tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj package FluentAssertions
dotnet add tests/Drm.Container.Tests/Drm.Container.Tests.csproj package FluentAssertions
dotnet add tests/Drm.Server.Tests/Drm.Server.Tests.csproj package FluentAssertions
dotnet add tests/Drm.Server.Tests/Drm.Server.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj package FluentAssertions
```

- [ ] **Step 5: Verify scaffold**

Run:

```bash
dotnet test Drm.sln
```

Expected: all default tests pass. On non-Windows hosts, Windows WPF projects may not build; if so run:

```bash
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
```

Expected: non-Windows projects pass.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "chore: scaffold DRM foundation solution"
```

## Task 2: Implement Domain Policy Model

**Files:**
- Create: `src/Drm.Domain/Ids.cs`
- Create: `src/Drm.Domain/Permissions.cs`
- Create: `src/Drm.Domain/Policy.cs`
- Create: `src/Drm.Domain/PolicyDecision.cs`
- Create: `src/Drm.Domain/PolicyEvaluator.cs`
- Create: `tests/Drm.Domain.Tests/PolicyEvaluatorTests.cs`

- [ ] **Step 1: Write failing policy tests**

Create `tests/Drm.Domain.Tests/PolicyEvaluatorTests.cs`:

```csharp
using Drm.Domain;
using FluentAssertions;

namespace Drm.Domain.Tests;

public sealed class PolicyEvaluatorTests
{
    [Fact]
    public void Allows_view_when_user_has_view_grant_and_file_is_not_expired()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.AllowedPermissions.Should().HaveFlag(Permission.View);
        decision.ReasonCode.Should().Be("allowed");
    }

    [Fact]
    public void Denies_when_requested_permission_is_not_granted()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.Print, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("permission_not_granted");
    }

    [Fact]
    public void Denies_when_policy_is_expired()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("expired");
    }

    [Fact]
    public void Denies_when_policy_is_revoked()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1)) with { Revoked = true };
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("revoked");
    }

    private static FilePolicy TestPolicy(Permission permissions, DateTimeOffset expiresAtUtc)
        => new(
            TestIds.Tenant,
            TestIds.File,
            expiresAtUtc,
            Revoked: false,
            Grants: [new FileGrant(TestIds.User, permissions)],
            WatermarkTemplate: "{user} {time} {file}");

    private static class TestIds
    {
        public static readonly TenantId Tenant = TenantId.New();
        public static readonly ProtectedFileId File = ProtectedFileId.New();
        public static readonly UserId User = UserId.New();
        public static readonly DeviceId Device = DeviceId.New();
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
```

Expected: compile fails because domain types are missing.

- [ ] **Step 3: Implement domain types**

Create `src/Drm.Domain/Ids.cs`:

```csharp
namespace Drm.Domain;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
}

public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.NewGuid());
}

public readonly record struct ProtectedFileId(Guid Value)
{
    public static ProtectedFileId New() => new(Guid.NewGuid());
}
```

Create `src/Drm.Domain/Permissions.cs`:

```csharp
namespace Drm.Domain;

[Flags]
public enum Permission
{
    None = 0,
    View = 1 << 0,
    Print = 1 << 1,
    Copy = 1 << 2,
    ExportOriginal = 1 << 3,
    Edit = 1 << 4,
    DeleteProtectedCopy = 1 << 5
}
```

Create `src/Drm.Domain/Policy.cs`:

```csharp
namespace Drm.Domain;

public sealed record FilePolicy(
    TenantId TenantId,
    ProtectedFileId FileId,
    DateTimeOffset ExpiresAtUtc,
    bool Revoked,
    IReadOnlyList<FileGrant> Grants,
    string WatermarkTemplate);

public sealed record FileGrant(UserId UserId, Permission Permissions);

public sealed record PolicyRequest(
    TenantId TenantId,
    ProtectedFileId FileId,
    UserId UserId,
    DeviceId DeviceId,
    Permission RequestedPermission,
    DateTimeOffset NowUtc);
```

Create `src/Drm.Domain/PolicyDecision.cs`:

```csharp
namespace Drm.Domain;

public sealed record PolicyDecision(
    bool Allowed,
    Permission AllowedPermissions,
    string ReasonCode,
    string? WatermarkTemplate)
{
    public static PolicyDecision Allow(Permission permissions, string watermarkTemplate)
        => new(true, permissions, "allowed", watermarkTemplate);

    public static PolicyDecision Deny(string reasonCode)
        => new(false, Permission.None, reasonCode, null);
}
```

Create `src/Drm.Domain/PolicyEvaluator.cs`:

```csharp
namespace Drm.Domain;

public static class PolicyEvaluator
{
    public static PolicyDecision Evaluate(FilePolicy policy, PolicyRequest request)
    {
        if (policy.TenantId != request.TenantId)
        {
            return PolicyDecision.Deny("tenant_mismatch");
        }

        if (policy.FileId != request.FileId)
        {
            return PolicyDecision.Deny("file_mismatch");
        }

        if (policy.Revoked)
        {
            return PolicyDecision.Deny("revoked");
        }

        if (request.NowUtc > policy.ExpiresAtUtc)
        {
            return PolicyDecision.Deny("expired");
        }

        var grant = policy.Grants.FirstOrDefault(g => g.UserId == request.UserId);
        if (grant is null)
        {
            return PolicyDecision.Deny("no_grant");
        }

        if ((grant.Permissions & request.RequestedPermission) != request.RequestedPermission)
        {
            return PolicyDecision.Deny("permission_not_granted");
        }

        return PolicyDecision.Allow(grant.Permissions, policy.WatermarkTemplate);
    }
}
```

- [ ] **Step 4: Verify policy tests pass**

Run:

```bash
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Drm.Domain tests/Drm.Domain.Tests
git commit -m "feat: add DRM policy evaluator"
```

## Task 3: Implement Envelope Encryption and Protected Container

**Files:**
- Create: `src/Drm.Crypto/AesGcmPayload.cs`
- Create: `src/Drm.Crypto/EnvelopeCrypto.cs`
- Create: `src/Drm.Container/ProtectedFileHeader.cs`
- Create: `src/Drm.Container/ProtectedFilePackage.cs`
- Create: `src/Drm.Container/ProtectedFileWriter.cs`
- Create: `src/Drm.Container/ProtectedFileReader.cs`
- Create: `tests/Drm.Crypto.Tests/EnvelopeCryptoTests.cs`
- Create: `tests/Drm.Container.Tests/ProtectedFilePackageTests.cs`

- [ ] **Step 1: Write failing crypto tests**

Create `tests/Drm.Crypto.Tests/EnvelopeCryptoTests.cs`:

```csharp
using Drm.Crypto;
using FluentAssertions;

namespace Drm.Crypto.Tests;

public sealed class EnvelopeCryptoTests
{
    [Fact]
    public void Encrypt_then_decrypt_round_trips_payload()
    {
        var key = EnvelopeCrypto.GenerateKey();
        var plaintext = "sensitive pdf bytes"u8.ToArray();

        var encrypted = EnvelopeCrypto.Encrypt(plaintext, key, "file:123"u8.ToArray());
        var decrypted = EnvelopeCrypto.Decrypt(encrypted, key, "file:123"u8.ToArray());

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Decrypt_rejects_wrong_associated_data()
    {
        var key = EnvelopeCrypto.GenerateKey();
        var encrypted = EnvelopeCrypto.Encrypt("payload"u8.ToArray(), key, "file:123"u8.ToArray());

        var action = () => EnvelopeCrypto.Decrypt(encrypted, key, "file:456"u8.ToArray());

        action.Should().Throw<System.Security.Cryptography.AuthenticationTagMismatchException>();
    }
}
```

- [ ] **Step 2: Implement crypto**

Create `src/Drm.Crypto/AesGcmPayload.cs`:

```csharp
namespace Drm.Crypto;

public sealed record AesGcmPayload(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
```

Create `src/Drm.Crypto/EnvelopeCrypto.cs`:

```csharp
using System.Security.Cryptography;

namespace Drm.Crypto;

public static class EnvelopeCrypto
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] GenerateKey()
    {
        return RandomNumberGenerator.GetBytes(KeySizeBytes);
    }

    public static AesGcmPayload Encrypt(byte[] plaintext, byte[] key, byte[] associatedData)
    {
        ValidateKey(key);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new AesGcmPayload(nonce, ciphertext, tag);
    }

    public static byte[] Decrypt(AesGcmPayload payload, byte[] key, byte[] associatedData)
    {
        ValidateKey(key);
        var plaintext = new byte[payload.Ciphertext.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);

        return plaintext;
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"Key must be {KeySizeBytes} bytes.", nameof(key));
        }
    }
}
```

- [ ] **Step 3: Verify crypto tests pass**

Run:

```bash
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
```

Expected: all crypto tests pass.

- [ ] **Step 4: Write failing container tests**

Create `tests/Drm.Container.Tests/ProtectedFilePackageTests.cs`:

```csharp
using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Container.Tests;

public sealed class ProtectedFilePackageTests
{
    [Fact]
    public void Write_then_read_round_trips_header_and_payload()
    {
        var tenantId = TenantId.New();
        var fileId = ProtectedFileId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var pdfBytes = "%PDF-1.7 test"u8.ToArray();

        using var stream = new MemoryStream();
        ProtectedFileWriter.Write(stream, tenantId, fileId, "application/pdf", fileKey, pdfBytes);
        stream.Position = 0;

        var package = ProtectedFileReader.Read(stream);
        var decrypted = package.Decrypt(fileKey);

        package.Header.TenantId.Should().Be(tenantId.Value);
        package.Header.FileId.Should().Be(fileId.Value);
        package.Header.ContentType.Should().Be("application/pdf");
        decrypted.Should().Equal(pdfBytes);
    }
}
```

- [ ] **Step 5: Implement protected container**

Create `src/Drm.Container/ProtectedFileHeader.cs`:

```csharp
namespace Drm.Container;

public sealed record ProtectedFileHeader(
    int Version,
    Guid TenantId,
    Guid FileId,
    string ContentType,
    DateTimeOffset CreatedAtUtc);
```

Create `src/Drm.Container/ProtectedFilePackage.cs`:

```csharp
using Drm.Crypto;

namespace Drm.Container;

public sealed record ProtectedFilePackage(ProtectedFileHeader Header, AesGcmPayload Payload)
{
    public byte[] Decrypt(byte[] fileKey)
    {
        return EnvelopeCrypto.Decrypt(Payload, fileKey, ProtectedFileAssociatedData.For(Header));
    }
}
```

Create `src/Drm.Container/ProtectedFileWriter.cs`:

```csharp
using System.Buffers.Binary;
using System.Text.Json;
using Drm.Crypto;
using Drm.Domain;

namespace Drm.Container;

public static class ProtectedFileWriter
{
    private static readonly byte[] Magic = "DRM1"u8.ToArray();

    public static void Write(Stream destination, TenantId tenantId, ProtectedFileId fileId, string contentType, byte[] fileKey, byte[] plaintext)
    {
        var header = new ProtectedFileHeader(1, tenantId.Value, fileId.Value, contentType, DateTimeOffset.UtcNow);
        var associatedData = ProtectedFileAssociatedData.For(header);
        var encrypted = EnvelopeCrypto.Encrypt(plaintext, fileKey, associatedData);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);

        destination.Write(Magic);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, headerBytes.Length);
        destination.Write(length);
        destination.Write(headerBytes);
        WriteBytes(destination, encrypted.Nonce);
        WriteBytes(destination, encrypted.Tag);
        WriteBytes(destination, encrypted.Ciphertext);
    }

    private static void WriteBytes(Stream destination, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        destination.Write(length);
        destination.Write(bytes);
    }
}
```

Create `src/Drm.Container/ProtectedFileReader.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Drm.Crypto;

namespace Drm.Container;

public static class ProtectedFileReader
{
    private static readonly byte[] Magic = "DRM1"u8.ToArray();

    public static ProtectedFilePackage Read(Stream source)
    {
        var magic = ReadExactly(source, Magic.Length);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a DRM protected file.");
        }

        var headerBytes = ReadBytes(source);
        var header = JsonSerializer.Deserialize<ProtectedFileHeader>(headerBytes)
            ?? throw new InvalidDataException("Missing protected file header.");

        var nonce = ReadBytes(source);
        var tag = ReadBytes(source);
        var ciphertext = ReadBytes(source);

        return new ProtectedFilePackage(header, new AesGcmPayload(nonce, ciphertext, tag));
    }

    private static byte[] ReadBytes(Stream source)
    {
        var lengthBytes = ReadExactly(source, 4);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length < 0)
        {
            throw new InvalidDataException("Negative section length.");
        }

        return ReadExactly(source, length);
    }

    private static byte[] ReadExactly(Stream source, int length)
    {
        var buffer = new byte[length];
        source.ReadExactly(buffer);
        return buffer;
    }
}

internal static class ProtectedFileAssociatedData
{
    public static byte[] For(ProtectedFileHeader header)
    {
        return Encoding.UTF8.GetBytes($"{header.Version}:{header.TenantId:N}:{header.FileId:N}:{header.ContentType}");
    }
}
```

- [ ] **Step 6: Verify container tests pass**

Run:

```bash
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
```

Expected: all crypto and container tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Drm.Crypto src/Drm.Container tests/Drm.Crypto.Tests tests/Drm.Container.Tests
git commit -m "feat: add encrypted protected file container"
```

## Task 4: Implement Management Server Policy, File, and Audit APIs

**Files:**
- Create: `src/Drm.Server/AppDbContext.cs`
- Create: `src/Drm.Server/Entities.cs`
- Create: `src/Drm.Server/ServerMode.cs`
- Create: `src/Drm.Server/SeedData.cs`
- Create: `src/Drm.Server/Endpoints/FilesEndpoints.cs`
- Create: `src/Drm.Server/Endpoints/PolicyEndpoints.cs`
- Create: `src/Drm.Server/Endpoints/AuditEndpoints.cs`
- Modify: `src/Drm.Server/Program.cs`
- Create: `tests/Drm.Server.Tests/PolicyApiTests.cs`

- [ ] **Step 1: Write failing server API tests**

Create `tests/Drm.Server.Tests/PolicyApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class PolicyApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PolicyApiTests(WebApplicationFactory<Program> factory)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"drm-policy-api-{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={dbPath}");
            builder.UseSetting("Drm:Mode", "OnPrem");
        });
    }

    [Fact]
    public async Task Registered_file_can_be_opened_by_granted_user()
    {
        var client = _factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId = userId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View, Print"
        });

        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var decision = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        decision.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await decision.Content.ReadFromJsonAsync<DecisionResponse>();
        body.Should().NotBeNull();
        body!.allowed.Should().BeTrue();
        body.reasonCode.Should().Be("allowed");
    }

    private sealed record DecisionResponse(bool allowed, string reasonCode);
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```bash
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
```

Expected: compile or runtime failure because APIs do not exist.

- [ ] **Step 3: Implement server persistence and endpoints**

Create `src/Drm.Server/ServerMode.cs`:

```csharp
namespace Drm.Server;

public enum ServerMode
{
    Saas,
    OnPrem
}
```

Create `src/Drm.Server/Entities.cs`:

```csharp
namespace Drm.Server;

public sealed class ProtectedFileEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string ContentType { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool Revoked { get; set; }
    public string Permissions { get; set; } = "";
    public string WatermarkTemplate { get; set; } = "{user} {time} {file}";
}

public sealed class AuditEventEntity
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? FileId { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

Create `src/Drm.Server/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ProtectedFileEntity> ProtectedFiles => Set<ProtectedFileEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
}
```

Create endpoint files with extension methods:

```csharp
// src/Drm.Server/Endpoints/FilesEndpoints.cs
using Drm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class FilesEndpoints
{
    public static IEndpointRouteBuilder MapFilesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/files", async (RegisterFileRequest request, AppDbContext db) =>
        {
            var entity = new ProtectedFileEntity
            {
                Id = request.fileId,
                TenantId = request.tenantId,
                OwnerUserId = request.ownerUserId,
                ContentType = request.contentType,
                ExpiresAtUtc = request.expiresAtUtc,
                Permissions = request.permissions,
                Revoked = false
            };

            db.ProtectedFiles.Add(entity);
            db.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.tenantId,
                FileId = request.fileId,
                UserId = request.ownerUserId,
                EventType = "file_registered",
                ReasonCode = "created",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
            return Results.Created($"/api/files/{request.fileId}", new { request.fileId });
        });

        app.MapPost("/api/files/{fileId:guid}/revoke", async (Guid fileId, AppDbContext db) =>
        {
            var file = await db.ProtectedFiles.SingleOrDefaultAsync(f => f.Id == fileId);
            if (file is null)
            {
                return Results.NotFound();
            }

            file.Revoked = true;
            db.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = file.TenantId,
                FileId = file.Id,
                UserId = file.OwnerUserId,
                EventType = "file_revoked",
                ReasonCode = "admin_revoked",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private sealed record RegisterFileRequest(
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType,
        DateTimeOffset expiresAtUtc,
        string permissions);
}
```

```csharp
// src/Drm.Server/Endpoints/PolicyEndpoints.cs
using Drm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/policy/decide", async (PolicyDecisionRequest request, AppDbContext db) =>
        {
            var file = await db.ProtectedFiles.SingleOrDefaultAsync(f => f.Id == request.fileId && f.TenantId == request.tenantId);
            if (file is null)
            {
                return Results.NotFound(new { allowed = false, reasonCode = "file_not_found" });
            }

            var permissions = Enum.Parse<Permission>(file.Permissions, ignoreCase: true);
            var policy = new FilePolicy(
                new TenantId(file.TenantId),
                new ProtectedFileId(file.Id),
                file.ExpiresAtUtc,
                file.Revoked,
                [new FileGrant(new UserId(file.OwnerUserId), permissions)],
                file.WatermarkTemplate);

            var requested = Enum.Parse<Permission>(request.requestedPermission, ignoreCase: true);
            var decision = PolicyEvaluator.Evaluate(policy, new PolicyRequest(
                new TenantId(request.tenantId),
                new ProtectedFileId(request.fileId),
                new UserId(request.userId),
                new DeviceId(request.deviceId),
                requested,
                DateTimeOffset.UtcNow));

            db.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.tenantId,
                FileId = request.fileId,
                UserId = request.userId,
                EventType = decision.Allowed ? "access_allowed" : "access_denied",
                ReasonCode = decision.ReasonCode,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                allowed = decision.Allowed,
                allowedPermissions = decision.AllowedPermissions.ToString(),
                reasonCode = decision.ReasonCode,
                watermarkTemplate = decision.WatermarkTemplate
            });
        });

        return app;
    }

    private sealed record PolicyDecisionRequest(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission);
}
```

```csharp
// src/Drm.Server/Endpoints/AuditEndpoints.cs
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", async (Guid tenantId, AppDbContext db) =>
            await db.AuditEvents
                .Where(e => e.TenantId == tenantId)
                .OrderByDescending(e => e.CreatedAtUtc)
                .Take(200)
                .ToListAsync());

        return app;
    }
}
```

Modify `src/Drm.Server/Program.cs`:

```csharp
using Drm.Server;
using Drm.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DrmDb") ?? "Data Source=drm.local.db";
    if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapFilesEndpoints();
app.MapPolicyEndpoints();
app.MapAuditEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Verify server API tests pass**

Run:

```bash
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
```

Expected: server API tests pass.

- [ ] **Step 5: Add revoke test**

Add to `PolicyApiTests`:

```csharp
[Fact]
public async Task Revoked_file_denies_future_open()
{
    var client = _factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    var userId = Guid.NewGuid();

    await client.PostAsJsonAsync("/api/files", new
    {
        tenantId,
        fileId,
        ownerUserId = userId,
        contentType = "application/pdf",
        expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        permissions = "View"
    });

    var revoke = await client.PostAsync($"/api/files/{fileId}/revoke", null);
    revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var decision = await client.PostAsJsonAsync("/api/policy/decide", new
    {
        tenantId,
        fileId,
        userId,
        deviceId = Guid.NewGuid(),
        requestedPermission = "View"
    });

    var body = await decision.Content.ReadFromJsonAsync<DecisionResponse>();
    body!.allowed.Should().BeFalse();
    body.reasonCode.Should().Be("revoked");
}
```

- [ ] **Step 6: Verify revoke test passes**

Run:

```bash
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
```

Expected: all server tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add management server policy APIs"
```

## Task 5: Implement Agent Core Protect/Open Workflows

**Files:**
- Create: `src/Drm.Agent.Core/DrmServerClient.cs`
- Create: `src/Drm.Agent.Core/ProtectPdfWorkflow.cs`
- Create: `src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs`
- Create: `tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs`

- [ ] **Step 1: Write failing workflow test**

Create `tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs`:

```csharp
using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class ProtectAndOpenWorkflowTests
{
    [Fact]
    public async Task Protect_registers_file_and_open_decrypts_when_policy_allows()
    {
        var server = new FakeDrmServerClient();
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var pdf = "%PDF-1.7"u8.ToArray();
        var fileKey = EnvelopeCrypto.GenerateKey();

        var protect = new ProtectPdfWorkflow(server);
        var protectedBytes = await protect.ProtectAsync(tenantId, userId, pdf, fileKey, CancellationToken.None);

        var open = new OpenProtectedPdfWorkflow(server);
        var opened = await open.OpenAsync(protectedBytes, userId, deviceId, fileKey, CancellationToken.None);

        opened.Content.Should().Equal(pdf);
        opened.Watermark.Should().Contain(userId.Value.ToString("N"));
    }

    private sealed class FakeDrmServerClient : IDrmServerClient
    {
        private Guid _tenantId;
        private Guid _fileId;
        private Guid _ownerUserId;

        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            _tenantId = tenantId;
            _fileId = fileId;
            _ownerUserId = ownerUserId;
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            if (tenantId == _tenantId && fileId == _fileId && userId == _ownerUserId && permission == Permission.View)
            {
                return Task.FromResult(new OpenDecision(true, "allowed", "{user} {file}", Permission.View));
            }

            return Task.FromResult(new OpenDecision(false, "denied", null, Permission.None));
        }
    }
}
```

- [ ] **Step 2: Implement workflow types**

Create `src/Drm.Agent.Core/DrmServerClient.cs`:

```csharp
using System.Net.Http.Json;
using Drm.Domain;

namespace Drm.Agent.Core;

public interface IDrmServerClient
{
    Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken);
    Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken);
}

public sealed record OpenDecision(bool Allowed, string ReasonCode, string? WatermarkTemplate, Permission AllowedPermissions);

public sealed class DrmServerClient(HttpClient httpClient) : IDrmServerClient
{
    public async Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType,
            expiresAtUtc,
            permissions = permissions.ToString()
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId,
            deviceId,
            requestedPermission = permission.ToString()
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DecisionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Server returned an empty policy decision.");

        var permissions = string.IsNullOrWhiteSpace(body.allowedPermissions)
            ? Permission.None
            : Enum.Parse<Permission>(body.allowedPermissions, ignoreCase: true);

        return new OpenDecision(body.allowed, body.reasonCode, body.watermarkTemplate, permissions);
    }

    private sealed record DecisionResponse(bool allowed, string reasonCode, string? watermarkTemplate, string? allowedPermissions);
}
```

Create `src/Drm.Agent.Core/ProtectPdfWorkflow.cs`:

```csharp
using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class ProtectPdfWorkflow(IDrmServerClient serverClient)
{
    public async Task<byte[]> ProtectAsync(TenantId tenantId, UserId ownerUserId, byte[] pdfBytes, byte[] fileKey, CancellationToken cancellationToken)
    {
        var fileId = ProtectedFileId.New();
        await serverClient.RegisterFileAsync(
            tenantId.Value,
            fileId.Value,
            ownerUserId.Value,
            "application/pdf",
            DateTimeOffset.UtcNow.AddDays(7),
            Permission.View | Permission.Print,
            cancellationToken);

        using var stream = new MemoryStream();
        ProtectedFileWriter.Write(stream, tenantId, fileId, "application/pdf", fileKey, pdfBytes);
        return stream.ToArray();
    }
}
```

Create `src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs`:

```csharp
using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class OpenProtectedPdfWorkflow(IDrmServerClient serverClient)
{
    public async Task<OpenedProtectedPdf> OpenAsync(byte[] protectedBytes, UserId userId, DeviceId deviceId, byte[] fileKey, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(protectedBytes);
        var package = ProtectedFileReader.Read(stream);

        var decision = await serverClient.DecideAsync(
            package.Header.TenantId,
            package.Header.FileId,
            userId.Value,
            deviceId.Value,
            Permission.View,
            cancellationToken);

        if (!decision.Allowed)
        {
            throw new UnauthorizedAccessException($"Access denied: {decision.ReasonCode}");
        }

        var pdf = package.Decrypt(fileKey);
        var watermark = (decision.WatermarkTemplate ?? "{user} {file}")
            .Replace("{user}", userId.Value.ToString("N"), StringComparison.Ordinal)
            .Replace("{file}", package.Header.FileId.ToString("N"), StringComparison.Ordinal)
            .Replace("{time}", DateTimeOffset.UtcNow.ToString("O"), StringComparison.Ordinal);

        return new OpenedProtectedPdf(pdf, watermark, decision.AllowedPermissions);
    }
}

public sealed record OpenedProtectedPdf(byte[] Content, string Watermark, Permission Permissions);
```

- [ ] **Step 3: Verify agent workflow tests pass**

Run:

```bash
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
```

Expected: all agent core tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Drm.Agent.Core tests/Drm.Agent.Core.Tests
git commit -m "feat: add agent protect and open workflows"
```

## Task 6: Add Windows Service and Viewer Shells

**Files:**
- Modify: `src/Drm.Agent.Service.Windows/Program.cs`
- Create: `src/Drm.Agent.Service.Windows/Worker.cs`
- Modify: `src/Drm.Viewer.Windows/MainWindow.xaml`
- Modify: `src/Drm.Viewer.Windows/MainWindow.xaml.cs`
- Modify: `src/Drm.Viewer.Windows/App.xaml`
- Modify: `src/Drm.Viewer.Windows/App.xaml.cs`

- [ ] **Step 1: Implement visible service heartbeat**

Set `src/Drm.Agent.Service.Windows/Program.cs`:

```csharp
using Drm.Agent.Service.Windows;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DRM Agent";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
```

Set `src/Drm.Agent.Service.Windows/Worker.cs`:

```csharp
namespace Drm.Agent.Service.Windows;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("DRM Agent heartbeat at {Time}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

- [ ] **Step 2: Implement viewer window with watermark surface**

Set `src/Drm.Viewer.Windows/MainWindow.xaml`:

```xml
<Window x:Class="Drm.Viewer.Windows.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="DRM Protected Viewer"
        Width="1100"
        Height="760"
        MinWidth="900"
        MinHeight="600">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="44" />
            <RowDefinition Height="*" />
            <RowDefinition Height="28" />
        </Grid.RowDefinitions>

        <DockPanel Grid.Row="0" Background="#1F2937" LastChildFill="False">
            <TextBlock Text="Protected PDF"
                       Foreground="White"
                       FontWeight="SemiBold"
                       VerticalAlignment="Center"
                       Margin="14,0" />
            <TextBlock x:Name="PermissionText"
                       Foreground="#D1D5DB"
                       VerticalAlignment="Center"
                       Margin="18,0" />
        </DockPanel>

        <Grid Grid.Row="1" Background="#111827">
            <WebBrowser x:Name="PdfHost" />
            <TextBlock x:Name="WatermarkText"
                       Foreground="#55FFFFFF"
                       FontSize="28"
                       FontWeight="SemiBold"
                       HorizontalAlignment="Center"
                       VerticalAlignment="Center"
                       IsHitTestVisible="False"
                       RenderTransformOrigin="0.5,0.5">
                <TextBlock.RenderTransform>
                    <RotateTransform Angle="-28" />
                </TextBlock.RenderTransform>
            </TextBlock>
        </Grid>

        <TextBlock Grid.Row="2"
                   x:Name="StatusText"
                   Background="#F3F4F6"
                   Foreground="#374151"
                   VerticalAlignment="Center"
                   Padding="10,0" />
    </Grid>
</Window>
```

Set `src/Drm.Viewer.Windows/MainWindow.xaml.cs`:

```csharp
using System.IO;
using System.Windows;

namespace Drm.Viewer.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PermissionText.Text = "View allowed";
        WatermarkText.Text = "user@example.com 2026-05-14 protected-file";
        StatusText.Text = "Viewer shell ready. PDF rendering integration follows after agent workflow wiring.";
    }

    public void LoadPdfFromTemporaryFile(string path, string watermark, string permissions)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Protected PDF render file was not found.", path);
        }

        PermissionText.Text = permissions;
        WatermarkText.Text = watermark;
        StatusText.Text = Path.GetFileName(path);
        PdfHost.Navigate(path);
    }
}
```

- [ ] **Step 3: Build Windows projects on a Windows host**

Run on Windows:

```powershell
dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```

Expected: both projects build. The viewer displays a visible watermark shell.

- [ ] **Step 4: Commit**

```bash
git add src/Drm.Agent.Service.Windows src/Drm.Viewer.Windows
git commit -m "feat: add Windows agent and viewer shells"
```

## Task 7: Add Local End-to-End Smoke Flow

**Files:**
- Create: `README.md`
- Create: `tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj`
- Create: `tests/Drm.Integration.Tests/ServerIntegratedWorkflowTests.cs`

- [ ] **Step 1: Create dedicated integration test project**

Run:

```bash
dotnet new xunit -n Drm.Integration.Tests -o tests/Drm.Integration.Tests
dotnet sln add tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj
dotnet add tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj reference src/Drm.Agent.Core/Drm.Agent.Core.csproj src/Drm.Server/Drm.Server.csproj
dotnet add tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj package FluentAssertions
dotnet add tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

Expected: server-integrated tests live in a dedicated integration test project and do not introduce an upward dependency from `Drm.Agent.Core.Tests` to `Drm.Server`.

- [ ] **Step 2: Add integrated test using test server**

Create `tests/Drm.Integration.Tests/ServerIntegratedWorkflowTests.cs`:

```csharp
using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Integration.Tests;

public sealed class ServerIntegratedWorkflowTests
{
    [Fact]
    public async Task Agent_core_can_protect_and_open_against_management_server()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"drm-integrated-{Guid.NewGuid():N}.db");
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={dbPath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
        var httpClient = factory.CreateClient();
        var serverClient = new DrmServerClient(httpClient);
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var pdfBytes = "%PDF-1.7 smoke"u8.ToArray();

        var protectedBytes = await new ProtectPdfWorkflow(serverClient)
            .ProtectAsync(tenantId, userId, pdfBytes, fileKey, CancellationToken.None);

        var opened = await new OpenProtectedPdfWorkflow(serverClient)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, CancellationToken.None);

        opened.Content.Should().Equal(pdfBytes);
        opened.Permissions.Should().HaveFlag(Permission.View);
    }
}
```

- [ ] **Step 3: Run integrated tests**

Run:

```bash
dotnet test tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj
```

Expected: integrated agent/server flow passes.

- [ ] **Step 4: Add README instructions**

Create or replace `README.md`:

```markdown
# Enterprise DRM

This repository contains an independently designed enterprise DRM/IRM platform.

## Foundation MVP

The first vertical slice protects PDF files into an encrypted container, registers file policy with the management server, checks policy before opening, applies watermark metadata, audits access, and supports revoke.

## Development Prerequisites

- .NET 10 SDK
- Windows 11 development host for WPF viewer/service work
- PostgreSQL for production-like deployments
- SQLite is used for local smoke tests

## Run Server

```bash
dotnet run --project src/Drm.Server/Drm.Server.csproj
```

Health check:

```bash
curl http://localhost:5000/healthz
```

## Run Tests

```bash
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
dotnet test tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj
```

Windows UI projects require a Windows host:

```powershell
dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```
```

- [ ] **Step 5: Commit**

```bash
git add README.md tests/Drm.Integration.Tests
git commit -m "test: add foundation smoke workflow"
```

## Task 8: Hardening Pass for MVP Boundaries

**Files:**
- Modify: `tests/Drm.Domain.Tests/PolicyEvaluatorTests.cs`
- Modify: `tests/Drm.Container.Tests/ProtectedFilePackageTests.cs`
- Create: `docs/security/mvp-threat-boundaries.md`

- [ ] **Step 1: Add security boundary doc**

Create `docs/security/mvp-threat-boundaries.md`:

```markdown
# MVP Threat Boundaries

The MVP is a visible enterprise DRM client/server foundation. It is not stealth software and does not delete arbitrary files.

## Enforced in MVP

- Protected files use authenticated encryption.
- Server policy is checked before opening.
- Revoked files are denied future opens.
- Audit events are recorded for registration, access decisions, and revocation.
- Windows agent/service is visible and signed in production builds.

## Not Enforced in MVP

- Perfect screenshot prevention.
- Kernel-level anti-tamper.
- Office/CAD/native app editing control.
- Transparent folder encryption.
- Arbitrary file deletion.

## Remote Delete Rule

Remote delete applies only to files whose container metadata proves they were created or enrolled by this platform. The agent must never accept a server command to delete a path that is not known as a protected-file inventory item.
```

- [ ] **Step 2: Add policy evaluator test for tenant mismatch**

Add to `tests/Drm.Domain.Tests/PolicyEvaluatorTests.cs`:

```csharp
[Fact]
public void Denies_when_tenant_does_not_match()
{
    var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
    var request = new PolicyRequest(TenantId.New(), TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

    var decision = PolicyEvaluator.Evaluate(policy, request);

    decision.Allowed.Should().BeFalse();
    decision.ReasonCode.Should().Be("tenant_mismatch");
}
```

- [ ] **Step 3: Add invalid container test**

Add to `tests/Drm.Container.Tests/ProtectedFilePackageTests.cs`:

```csharp
[Fact]
public void Reader_rejects_non_drm_file()
{
    using var stream = new MemoryStream("not drm"u8.ToArray());

    var action = () => ProtectedFileReader.Read(stream);

    action.Should().Throw<InvalidDataException>().WithMessage("Not a DRM protected file.");
}
```

- [ ] **Step 4: Run all non-Windows tests**

Run:

```bash
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
```

Expected: all non-Windows tests pass.

- [ ] **Step 5: Commit**

```bash
git add docs/security tests
git commit -m "docs: define MVP security boundaries"
```

## Task 9: Final Verification and Handoff

**Files:**
- Modify: `README.md` if commands changed during implementation

- [ ] **Step 1: Verify git state**

Run:

```bash
git status --short
```

Expected: either clean, or only intentional uncommitted changes that will be committed in Step 4.

- [ ] **Step 2: Run full non-Windows verification**

Run:

```bash
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 3: Run Windows verification on a Windows host**

Run:

```powershell
dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```

Expected: all Windows projects build.

- [ ] **Step 4: Commit final doc or verification updates**

```bash
git add README.md docs
git commit -m "docs: update foundation MVP handoff"
```

If there are no changes to commit, skip this step and record that the tree is clean.

## Self-Review Notes

- Spec coverage: this plan covers the Phase 1 foundation MVP: management server, server mode configuration, policy APIs, audit events, PDF protection container, protected PDF open workflow, Windows service shell, Windows viewer shell, revoke, and security boundary docs.
- Intentional gaps: AD/Entra, SAML/OIDC, SCIM, Outlook add-in, Office/CAD, shared-folder auto-encryption, transparent encryption, browser viewer, advanced endpoint controls, and production KMS/HSM are deferred to separate plans.
- Stack decision: .NET 10 LTS is chosen because Microsoft lists .NET 10 as active LTS with support through November 14, 2028.

## References

- Approved design spec: `docs/superpowers/specs/2026-05-14-enterprise-drm-design.md`
- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
