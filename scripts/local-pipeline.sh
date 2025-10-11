#!/usr/bin/env bash
set -euo pipefail

echo "==> Restoring solution"
dotnet restore SteamBacklogPicker.sln

echo "==> Running domain tests"
dotnet test tests/Domain/Domain.Tests/Domain.Tests.csproj "$@"

echo "==> Running infrastructure tests"
dotnet test tests/Infrastructure/SteamDiscovery.Tests/SteamDiscovery.Tests.csproj "$@"

echo "==> Running integration tests (SteamClientAdapter)"
dotnet test tests/Integration/SteamClientAdapter.Tests/SteamClientAdapter.Tests.csproj "$@"

echo "==> Local pipeline completed"
