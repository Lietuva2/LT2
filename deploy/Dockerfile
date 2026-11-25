# syntax=docker/dockerfile:1
# One build, three images: web, worker and migrator. Build from the repository root (.dockerignore keeps bin/, obj/ and
# secrets out of the context):
#   docker build -f deploy/Dockerfile --target web .
# Runtime images are chiselled (no shell, no package manager) and run as the non-root "app" user (UID 1654).
# The "-extra" variant carries ICU and time-zone data, needed for Lithuanian culture and Europe/Vilnius schedules.

# The SDK is the one global.json names: the lock files carry the package versions that SDK brings with it (the Blazor
# build tools), so a newer SDK would fail the locked restore. Change both together (design 6.7).
FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig LT2.sln ./
# The project files first, so the restore is a layer of its own that a change to the code does not repeat.
COPY src/Lt2.Api.Contracts/Lt2.Api.Contracts.csproj src/Lt2.Api.Contracts/packages.lock.json src/Lt2.Api.Contracts/
COPY src/Lt2.Hosting/Lt2.Hosting.csproj src/Lt2.Hosting/packages.lock.json src/Lt2.Hosting/
COPY src/Lt2.Jobs/Lt2.Jobs.csproj src/Lt2.Jobs/packages.lock.json src/Lt2.Jobs/
COPY src/Lt2.SharedKernel/Lt2.SharedKernel.csproj src/Lt2.SharedKernel/packages.lock.json src/Lt2.SharedKernel/
COPY src/Lt2.Web/Lt2.Web.csproj src/Lt2.Web/packages.lock.json src/Lt2.Web/
COPY src/Lt2.Web.Client/Lt2.Web.Client.csproj src/Lt2.Web.Client/packages.lock.json src/Lt2.Web.Client/
COPY src/Lt2.Worker/Lt2.Worker.csproj src/Lt2.Worker/packages.lock.json src/Lt2.Worker/
COPY src/Lt2.Migrator/Lt2.Migrator.csproj src/Lt2.Migrator/packages.lock.json src/Lt2.Migrator/
COPY src/Modules/Lt2.Appeals/Lt2.Appeals.csproj src/Modules/Lt2.Appeals/packages.lock.json src/Modules/Lt2.Appeals/
COPY src/Modules/Lt2.Audit/Lt2.Audit.csproj src/Modules/Lt2.Audit/packages.lock.json src/Modules/Lt2.Audit/
COPY src/Modules/Lt2.Identity/Lt2.Identity.csproj src/Modules/Lt2.Identity/packages.lock.json src/Modules/Lt2.Identity/
COPY src/Modules/Lt2.Notifications/Lt2.Notifications.csproj src/Modules/Lt2.Notifications/packages.lock.json src/Modules/Lt2.Notifications/
COPY src/Modules/Lt2.Parliament/Lt2.Parliament.csproj src/Modules/Lt2.Parliament/packages.lock.json src/Modules/Lt2.Parliament/
COPY src/Modules/Lt2.Usage/Lt2.Usage.csproj src/Modules/Lt2.Usage/packages.lock.json src/Modules/Lt2.Usage/
# This restore only fills the layer. It cannot run in locked mode: from the project files alone it lacks the packages the Web
# SDK adds once it sees the .razor files, so it differs from the lock files. The restore that counts is the one in the
# publish step below, in locked mode (design 6.7).
RUN dotnet restore src/Lt2.Web/Lt2.Web.csproj \
 && dotnet restore src/Lt2.Worker/Lt2.Worker.csproj \
 && dotnet restore src/Lt2.Migrator/Lt2.Migrator.csproj
COPY src/ src/
# Publish restores again, now that the sources are here, in locked mode (each project's packages.lock.json fixes the
# package versions; a restore that would change one fails): the Web SDK adds the framework's Blazor assets (blazor.web.js)
# only when it sees the .razor files, so the restore above, made from the project files alone, lacks them. The packages
# are already in the layer above, so this restore downloads nothing.
RUN dotnet publish src/Lt2.Web/Lt2.Web.csproj -c Release -o /out/web -p:RestoreLockedMode=true \
 && dotnet publish src/Lt2.Worker/Lt2.Worker.csproj -c Release -o /out/worker -p:RestoreLockedMode=true \
 && dotnet publish src/Lt2.Migrator/Lt2.Migrator.csproj -c Release -o /out/migrator -p:RestoreLockedMode=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    ASPNETCORE_ENVIRONMENT=Production
USER app

FROM runtime AS web
WORKDIR /app
COPY --from=build --chown=root:root /out/web ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lt2.Web.dll"]

FROM runtime AS worker
WORKDIR /app
COPY --from=build --chown=root:root /out/worker ./
ENTRYPOINT ["dotnet", "Lt2.Worker.dll"]

FROM runtime AS migrator
WORKDIR /app
COPY --from=build --chown=root:root /out/migrator ./
ENTRYPOINT ["dotnet", "Lt2.Migrator.dll"]
