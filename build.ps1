$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $root "Build"
$backendOut = Join-Path $buildDir "Backend"

if (-not (Test-Path $buildDir)) {
  New-Item -ItemType Directory -Path $buildDir | Out-Null
}

Write-Host "Publishing .NET backend (self-contained)..." -ForegroundColor Cyan
dotnet publish "$root\\Back\\src\\Api\\SchedulerPDV.Api.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained false `
  /p:PublishSingleFile=true `
  /p:PublishTrimmed=false `
  /p:PublishReadyToRun=true `
  -o $backendOut

Write-Host "Building Electron portable executable..." -ForegroundColor Cyan
Push-Location "$root\\Front\\schedulerpdv-front"
npm run dist:portable
Pop-Location

Write-Host "Build output:" -ForegroundColor Green
Write-Host " - Backend: $backendOut"
Write-Host " - Electron EXE: $buildDir"
