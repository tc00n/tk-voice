# Builds the TK Voice release: tests, self-contained publish (TKVoice.exe incl. .NET runtime) and MSI.
# Output: artifacts\TKVoice-<version>-x64.msi
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
# No trailing backslash: Windows PowerShell would pass it as an escaped quote to dotnet.
$publishDir = Join-Path $root "artifacts\publish"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)." }
}

Invoke-Step "Tests" { dotnet test (Join-Path $root "TKVoice.slnx") -c $Configuration }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Step "Publish TKVoice.exe" {
    dotnet publish (Join-Path $root "src\TKVoice.App\TKVoice.App.csproj") -c $Configuration -r win-x64 `
        --self-contained true -p:PublishReadyToRun=true -o $publishDir
}

# Debug symbols are not needed on user machines.
Get-ChildItem $publishDir -Filter *.pdb | Remove-Item

Invoke-Step "Build MSI" {
    dotnet build (Join-Path $root "installer\TKVoice.Installer.wixproj") -c $Configuration "-p:PublishDir=$publishDir" `
        -o (Join-Path $root "artifacts")
}

Get-ChildItem (Join-Path $root "artifacts") -Filter *.msi | ForEach-Object { "MSI: $($_.FullName) ($([Math]::Round($_.Length / 1MB, 1)) MB)" }
