# syntax=docker/dockerfile:1.7
# ----------------------------------------------------------------------------
# Multi-stage build for Drm.Server (the on-prem / SaaS HTTP server).
# Other DRM components (FolderWatcher.Service, Viewer.Windows, Tray, Agent)
# target Windows and are not packaged in this image.
# ----------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — copy only csproj files first so the layer is cacheable.
COPY src/Drm.Domain/Drm.Domain.csproj src/Drm.Domain/
COPY src/Drm.Crypto/Drm.Crypto.csproj src/Drm.Crypto/
COPY src/Drm.Container/Drm.Container.csproj src/Drm.Container/
COPY src/Drm.Server/Drm.Server.csproj src/Drm.Server/

RUN dotnet restore src/Drm.Server/Drm.Server.csproj

# Build & publish — copy full sources.
COPY src/Drm.Domain/ src/Drm.Domain/
COPY src/Drm.Crypto/ src/Drm.Crypto/
COPY src/Drm.Container/ src/Drm.Container/
COPY src/Drm.Server/ src/Drm.Server/

RUN dotnet publish src/Drm.Server/Drm.Server.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ----------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Run as a non-root user for defense-in-depth.
RUN groupadd --system --gid 1001 drm && \
    useradd --system --uid 1001 --gid drm --no-create-home --shell /sbin/nologin drm && \
    mkdir -p /var/lib/drm && \
    chown drm:drm /var/lib/drm

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_RUNNING_IN_CONTAINER=true

WORKDIR /app
COPY --from=build /app/publish .
USER drm

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD bash -c 'echo > /dev/tcp/localhost/8080' || exit 1

ENTRYPOINT ["dotnet", "Drm.Server.dll"]
