#!/usr/bin/env bash
set -euo pipefail

echo "==> Restoring solution"
dotnet restore SteamBacklogPicker.sln
dotnet restore tests/Presentation/SteamBacklogPicker.Linux.Tests/SteamBacklogPicker.Linux.Tests.csproj

echo "==> Building common projects"
dotnet build src/Domain/Domain.csproj -c Release --no-restore -f net8.0
dotnet build src/Integration/ValveFormatParser/ValveFormatParser.csproj -c Release --no-restore -f net8.0
dotnet build src/Integration/SteamClientAdapter/SteamClientAdapter.csproj -c Release --no-restore -f net8.0
dotnet build src/Integration/SteamHooks/SteamHooks.csproj -c Release --no-restore -f net8.0
dotnet build src/Infrastructure/Telemetry/Telemetry.csproj -c Release --no-restore -f net8.0
dotnet build src/Infrastructure/SteamDiscovery/SteamDiscovery.csproj -c Release --no-restore -f net8.0
dotnet build src/Presentation/SteamBacklogPicker.AppCore/SteamBacklogPicker.AppCore.csproj -c Release --no-restore -f net8.0
dotnet build src/Presentation/SteamBacklogPicker.Linux/SteamBacklogPicker.Linux.csproj -c Release --no-restore -f net8.0

echo "==> Running domain tests"
dotnet test tests/Domain/Domain.Tests/Domain.Tests.csproj -c Release --no-restore -f net8.0 "$@"

echo "==> Running infrastructure tests"
dotnet test tests/Infrastructure/SteamDiscovery.Tests/SteamDiscovery.Tests.csproj -c Release --no-restore -f net8.0 "$@"

echo "==> Running catalog tests"
dotnet test tests/Integration/SteamCatalog.Tests/SteamCatalog.Tests.csproj -c Release --no-restore -f net8.0 "$@"

echo "==> Running integration tests (SteamClientAdapter)"
dotnet test tests/Integration/SteamClientAdapter.Tests/SteamClientAdapter.Tests.csproj -c Release --no-restore -f net8.0 "$@"

echo "==> Running integration tests (SteamHooks)"
dotnet test tests/Integration/SteamHooks.Tests/SteamHooks.Tests.csproj -c Release --no-restore -f net8.0 "$@"

echo "==> Running Linux presentation tests"
dotnet test tests/Presentation/SteamBacklogPicker.Linux.Tests/SteamBacklogPicker.Linux.Tests.csproj -c Release --no-restore -f net8.0 "$@"

echo "==> Local pipeline completed"
