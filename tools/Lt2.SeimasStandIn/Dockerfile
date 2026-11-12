# syntax=docker/dockerfile:1
# The Seimas open-data stand-in, LOCAL ONLY. Built by deploy/compose.yaml in the "local" profile, never by
# deploy/Dockerfile, so no production image can contain it. Build context: the repository root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY tools/Lt2.SeimasStandIn/ tools/Lt2.SeimasStandIn/
COPY tests/Lt2.Parliament.ImportTests/Seimas/Synthetic/ tests/Lt2.Parliament.ImportTests/Seimas/Synthetic/
RUN dotnet publish tools/Lt2.SeimasStandIn/Lt2.SeimasStandIn.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
ENV DOTNET_NOLOGO=1 \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
USER app
WORKDIR /app
COPY --from=build --chown=root:root /out ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lt2.SeimasStandIn.dll"]
