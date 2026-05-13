#!/usr/bin/env pwsh
#
# Run a SonarQube Community analysis against the local Docker instance.
#
# Prerequisites:
#   1. docker compose -f docker-compose.sonar.yml up -d
#   2. Generate a token at http://localhost:9000 (My Account -> Security)
#   3. setx SONAR_TOKEN "<token>"  then open a NEW terminal so the variable is visible
#
# This script wipes ./coverage, runs begin -> build -> tests with coverage -> end,
# and prints the project dashboard URL.

$ErrorActionPreference = 'Stop'

function Assert-LastExitCode {
    param([string]$Step)
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

$ProjectKey  = 'VisuAuth'
$ProjectName = 'VisuAuth'
$SonarHost   = 'http://localhost:9000'
$Solution    = 'src/VisuAuth.slnx'
$CoverageDir = './coverage'

if (-not $env:SONAR_TOKEN) {
    throw "SONAR_TOKEN is not set. Generate a token at $SonarHost (My Account -> Security) and persist with: setx SONAR_TOKEN '<token>'"
}

try {
    $status = Invoke-RestMethod -Uri "$SonarHost/api/system/status" -TimeoutSec 5
} catch {
    throw "SonarQube is not reachable on $SonarHost. Start it with: docker compose -f docker-compose.sonar.yml up -d"
}
if ($status.status -ne 'UP') {
    throw "SonarQube status is '$($status.status)'. Wait for the server to finish booting and retry."
}

if (-not (dotnet tool list -g | Select-String 'dotnet-sonarscanner')) {
    Write-Host 'Installing dotnet-sonarscanner global tool...' -ForegroundColor Cyan
    dotnet tool install --global dotnet-sonarscanner | Out-Null
}

if (Test-Path $CoverageDir) {
    Remove-Item $CoverageDir -Recurse -Force
}

Write-Host "Running Sonar analysis against $SonarHost (project '$ProjectKey')..." -ForegroundColor Cyan

dotnet sonarscanner begin `
    /k:"$ProjectKey" `
    /n:"$ProjectName" `
    /d:sonar.token="$env:SONAR_TOKEN" `
    /d:sonar.host.url="$SonarHost" `
    /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml" `
    /d:sonar.coverage.exclusions="samples/**,tests/**" `
    /d:sonar.exclusions="samples/**"
Assert-LastExitCode 'sonarscanner begin'

dotnet build $Solution -c Release
Assert-LastExitCode 'dotnet build'

dotnet test tests/VisuAuth.UnitTests/VisuAuth.UnitTests.csproj `
    -c Release --no-build --verbosity minimal `
    --collect:"XPlat Code Coverage" `
    --results-directory $CoverageDir `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
Assert-LastExitCode 'unit tests'

dotnet test tests/VisuAuth.IntegrationTests/VisuAuth.IntegrationTests.csproj `
    -c Release --no-build --verbosity minimal `
    --collect:"XPlat Code Coverage" `
    --results-directory $CoverageDir `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
Assert-LastExitCode 'integration tests'

dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
Assert-LastExitCode 'sonarscanner end'

Write-Host ''
Write-Host "Done. Results: $SonarHost/dashboard?id=$ProjectKey" -ForegroundColor Green
