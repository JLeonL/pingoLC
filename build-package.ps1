param(
    [string]$Configuration = "Release",
    [string]$AssetBundlePath = ".\AssetBundles\pingoenemyassets",
    [string]$OutputDirectory = ".\dist",
    [switch]$NoAssetBundle
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "PingoEnemy.csproj"
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
$dist = Join-Path $root $OutputDirectory
$packageRoot = Join-Path $dist "PingoEnemy"
$pluginRoot = Join-Path $packageRoot "BepInEx\plugins\PingoEnemy"
$dll = Join-Path $root "bin\$Configuration\netstandard2.1\PingoEnemy.dll"
$bundlePath = Join-Path $root $AssetBundlePath

if (!(Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet_cli"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_PACKAGES = Join-Path $root ".nuget\packages"
$env:APPDATA = Join-Path $root ".appdata\Roaming"
$env:LOCALAPPDATA = Join-Path $root ".appdata\Local"

& $dotnet build $project -c $Configuration --configfile (Join-Path $root "NuGet.Config")
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (!(Test-Path -LiteralPath $dll)) {
    throw "Missing compiled DLL: $dll"
}

if (!$NoAssetBundle -and !(Test-Path -LiteralPath $bundlePath)) {
    throw "Missing AssetBundle: $bundlePath. Use -NoAssetBundle to package the placeholder no-Unity version."
}

Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $root "manifest.json") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "icon.png") -Destination $packageRoot
Copy-Item -LiteralPath $dll -Destination $pluginRoot
Copy-Item -LiteralPath (Join-Path $root "assets\pingo.mp3") -Destination $pluginRoot
if (!$NoAssetBundle) {
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $bundlePath) -Destination (Join-Path $pluginRoot "pingoenemyassets")
}

$zip = Join-Path $dist "PingoEnemy.zip"
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -Force
Write-Host "Created $zip"
