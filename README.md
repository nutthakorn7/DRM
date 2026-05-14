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

Use the repository-local .NET SDK path when running commands:

```bash
PATH=/Users/pop7/.dotnet:$PATH
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
dotnet test tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj
```

On non-Windows hosts, Windows-targeted projects use `EnableWindowsTargeting` narrowly in their project files so restore and build can run on macOS.

Full solution:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test Drm.sln
```

Windows UI projects:

```powershell
dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```
