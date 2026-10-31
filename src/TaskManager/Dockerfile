# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY Directory.Build.props Directory.Packages.props ./
COPY TaskManager.SharedKernel/TaskManager.SharedKernel.csproj TaskManager.SharedKernel/
COPY TaskManager.Application.Abstractions/TaskManager.Application.Abstractions.csproj TaskManager.Application.Abstractions/
COPY TaskManager.Infrastructure/TaskManager.Infrastructure.csproj TaskManager.Infrastructure/
COPY TaskManager.Api/TaskManager.Api.csproj TaskManager.Api/
COPY Modules/Events/Events.Domain/Events.Domain.csproj Modules/Events/Events.Domain/
COPY Modules/Events/Events.Application/Events.Application.csproj Modules/Events/Events.Application/
COPY Modules/Turns/Turns.Domain/Turns.Domain.csproj Modules/Turns/Turns.Domain/
COPY Modules/Turns/Turns.Application/Turns.Application.csproj Modules/Turns/Turns.Application/

RUN dotnet restore TaskManager.Api/TaskManager.Api.csproj

COPY . .

FROM build AS migrations
RUN dotnet tool install --global dotnet-ef --version 10.0.4
ENV PATH="$PATH:/root/.dotnet/tools"
ENTRYPOINT ["dotnet", "ef", "database", "update", \
    "--project", "TaskManager.Infrastructure", \
    "--startup-project", "TaskManager.Infrastructure", \
    "--configuration", "Release"]

FROM build AS publish
RUN dotnet publish TaskManager.Api/TaskManager.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        curl \
        tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=60s --retries=6 \
    CMD curl -fsS http://127.0.0.1:8080/api/v1/health >/dev/null || exit 1

USER $APP_UID
ENTRYPOINT ["dotnet", "TaskManager.Api.dll"]
