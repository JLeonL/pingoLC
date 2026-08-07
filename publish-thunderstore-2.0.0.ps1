$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet7 = Join-Path $root ".dotnet7"
$tcli = Join-Path $root ".dotnet_cli\.dotnet\tools\tcli.exe"
$package = Join-Path $root "dist\PingoEnemy.zip"
$config = Join-Path $root "thunderstore.toml"

if (!(Test-Path -LiteralPath $tcli)) {
    throw "Missing tcli.exe. Run: .\.dotnet\dotnet.exe tool install --global tcli"
}

if (!(Test-Path -LiteralPath $package)) {
    throw "Missing package ZIP: $package"
}

if (!(Test-Path -LiteralPath $config)) {
    throw "Missing Thunderstore project config: $config"
}

if (!(Test-Path -LiteralPath (Join-Path $dotnet7 "dotnet.exe"))) {
    throw "Missing local .NET 7 runtime: $dotnet7"
}

$env:DOTNET_ROOT = $dotnet7
$env:PATH = "$dotnet7;$env:PATH"

Write-Host ""
Write-Host "Publishing PingoEnemy 2.0.0 to Thunderstore..." -ForegroundColor Cyan
Write-Host "Package: $package"
Write-Host "Namespace/team: Ctb_Eivissa"
Write-Host "Name: PingoEnemy"
Write-Host ""
Write-Host "Paste your Thunderstore Service Account token below."
Write-Host "It will not be saved by this script."
Write-Host ""

$secureToken = Read-Host "Thunderstore token" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
}
finally {
    if ($bstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Token cannot be empty."
}

& $tcli publish `
    --file "$package" `
    --config-path "$config" `
    --token "$token"

$exitCode = $LASTEXITCODE
$token = $null
[GC]::Collect()

if ($exitCode -ne 0) {
    throw "tcli publish failed with exit code $exitCode"
}

Write-Host ""
Write-Host "PingoEnemy 2.0.0 published successfully." -ForegroundColor Green
Read-Host "Press Enter to close"
