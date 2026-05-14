# Enterprise DRM Foundation

Task 1 scaffolds the foundation solution, shared build settings, source projects, and test projects for the Enterprise DRM MVP.

## Build

Use the repository-local .NET SDK path when running commands:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test Drm.sln
```

On non-Windows hosts, Windows-targeted projects use `EnableWindowsTargeting` narrowly in their project files so restore and build can run on macOS.
