# syntax=docker/dockerfile:1
# One build, three images: web, worker and migrator. Build from the repository root (.dockerignore keeps bin/, obj/ and
# secrets out of the context):
#   docker build -f deploy/Dockerfile --target web .
# Runtime images are chiselled (no shell, no package manager) and run as the non-root "app" user (UID 1654).
# The "-extra" variant carries ICU and time-zone data, needed for Lithuanian culture and Europe/Vilnius schedules.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig LT2.sln ./
# The project files first, so the restore is a layer of its own that a change to the code does not repeat.
COPY src/Lt2.Api.Contracts/Lt2.Api.Contracts.csproj src/Lt2.Api.Contracts/
COPY src/Lt2.Hosting/Lt2.Hosting.csproj src/Lt2.Hosting/
COPY src/Lt2.SharedKernel/Lt2.SharedKernel.csproj src/Lt2.SharedKernel/
COPY src/Lt2.Web/Lt2.Web.csproj src/Lt2.Web/
COPY src/Lt2.Web.Client/Lt2.Web.Client.csproj src/Lt2.Web.Client/
COPY src/Lt2.Worker/Lt2.Worker.csproj src/Lt2.Worker/
COPY src/Lt2.Migrator/Lt2.Migrator.csproj src/Lt2.Migrator/
COPY src/Modules/Lt2.Appeals/Lt2.Appeals.csproj src/Modules/Lt2.Appeals/
COPY src/Modules/Lt2.Audit/Lt2.Audit.csproj src/Modules/Lt2.Audit/
COPY src/Modules/Lt2.Identity/Lt2.Identity.csproj src/Modules/Lt2.Identity/
COPY src/Modules/Lt2.Notifications/Lt2.Notifications.csproj src/Modules/Lt2.Notifications/
COPY src/Modules/Lt2.Parliament/Lt2.Parliament.csproj src/Modules/Lt2.Parliament/
COPY src/Modules/Lt2.Usage/Lt2.Usage.csproj src/Modules/Lt2.Usage/
RUN dotnet restore src/Lt2.Web/Lt2.Web.csproj \
 && dotnet restore src/Lt2.Worker/Lt2.Worker.csproj \
 && dotnet restore src/Lt2.Migrator/Lt2.Migrator.csproj
COPY src/ src/
# Publish restores again, now that the sources are here: the Web SDK adds the framework's Blazor assets (blazor.web.js)
# only when it sees the .razor files, so the restore above, made from the project files alone, lacks them. The packages
# are already in the layer above, so this restore downloads nothing.
RUN dotnet publish src/Lt2.Web/Lt2.Web.csproj -c Release -o /out/web \
 && dotnet publish src/Lt2.Worker/Lt2.Worker.csproj -c Release -o /out/worker \
 && dotnet publish src/Lt2.Migrator/Lt2.Migrator.csproj -c Release -o /out/migrator

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
