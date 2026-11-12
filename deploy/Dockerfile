# syntax=docker/dockerfile:1
# One build, three images: web, worker and migrator. Build from the repository root:
#   docker build -f deploy/Dockerfile --target web .
# Runtime images are chiselled (no shell, no package manager) and run as the non-root "app" user (UID 1654).
# The "-extra" variant carries ICU and time-zone data, needed for Lithuanian culture and Europe/Vilnius schedules.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig LT2.sln ./
COPY src/ src/
RUN dotnet restore src/Lt2.Web/Lt2.Web.csproj \
 && dotnet restore src/Lt2.Worker/Lt2.Worker.csproj \
 && dotnet restore src/Lt2.Migrator/Lt2.Migrator.csproj
RUN dotnet publish src/Lt2.Web/Lt2.Web.csproj -c Release --no-restore -o /out/web \
 && dotnet publish src/Lt2.Worker/Lt2.Worker.csproj -c Release --no-restore -o /out/worker \
 && dotnet publish src/Lt2.Migrator/Lt2.Migrator.csproj -c Release --no-restore -o /out/migrator

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
