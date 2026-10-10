<#
.SYNOPSIS
    Build Ask the Almanac from source and install it into Stardew Valley's Mods folder (Windows).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File install.ps1
    powershell -ExecutionPolicy Bypass -File install.ps1 -InstallSmapi
    powershell -ExecutionPolicy Bypass -File install.ps1 -GamePath "D:\Games\Stardew Valley"
#>
param(
    # Stardew Valley folder, if it isn't found automatically.
    [string]$GamePath = "",
    # Download and install the latest SMAPI if it's missing.
    [switch]$InstallSmapi
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Info($message) { Write-Host "==> $message" -ForegroundColor Green }
function Fail($message) { Write-Host "Error: $message" -ForegroundColor Red; exit 1 }

# Find the Stardew Valley folder (the one containing "Stardew Valley.dll").
function Find-Game {
    $candidates = @()

    # Steam: the install path from the registry, plus every library in libraryfolders.vdf
    $steamRoots = @("${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam")
    foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam") {
        $steam = (Get-ItemProperty -Path $key -ErrorAction SilentlyContinue)
        if ($steam.SteamPath) { $steamRoots += $steam.SteamPath }
        if ($steam.InstallPath) { $steamRoots += $steam.InstallPath }
    }
    foreach ($root in $steamRoots | Select-Object -Unique) {
        $candidates += Join-Path $root "steamapps\common\Stardew Valley"
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($match in Select-String -Path $vdf -Pattern '"path"\s+"(.+)"') {
                $library = $match.Matches[0].Groups[1].Value -replace '\\\\', '\'
                $candidates += Join-Path $library "steamapps\common\Stardew Valley"
            }
        }
    }

    # GOG and Xbox app
    $candidates += "${env:ProgramFiles(x86)}\GOG Galaxy\Games\Stardew Valley"
    $candidates += "${env:ProgramFiles(x86)}\GOG Games\Stardew Valley"
    $candidates += "C:\XboxGames\Stardew Valley\Content"

    foreach ($dir in $candidates) {
        if (Test-Path (Join-Path $dir "Stardew Valley.dll")) { return $dir }
    }
    return ""
}

# Download the latest SMAPI release and run its installer non-interactively.
function Install-Smapi($gamePath) {
    $release = Invoke-RestMethod "https://api.github.com/repos/Pathoschild/SMAPI/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -match '-installer\.zip$' -and $_.name -notmatch 'double' } | Select-Object -First 1
    if (-not $asset) { Fail "Couldn't find the latest SMAPI release. Install it manually from https://smapi.io." }

    $temp = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid())
    New-Item -ItemType Directory $temp | Out-Null
    try {
        Info "Downloading $($asset.name)"
        Invoke-WebRequest $asset.browser_download_url -OutFile "$temp\smapi.zip"
        Expand-Archive "$temp\smapi.zip" -DestinationPath $temp
        $installer = Get-ChildItem $temp -Recurse -Filter "SMAPI.Installer.exe" | Where-Object { $_.FullName -match '\\internal\\windows\\' } | Select-Object -First 1
        if (-not $installer) { Fail "The SMAPI download didn't contain a Windows installer." }

        Info "Installing SMAPI into $gamePath"
        & $installer.FullName --install --game-path "$gamePath" --no-prompt
        if ($LASTEXITCODE -ne 0) { Fail "The SMAPI installer failed." }
    }
    finally {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 1. .NET SDK
Info "Checking for the .NET SDK"
$sdks = if (Get-Command dotnet -ErrorAction SilentlyContinue) { & dotnet --list-sdks } else { @() }
if (-not ($sdks | Where-Object { [int]($_.Split('.')[0]) -ge 6 })) {
    Write-Host "The .NET SDK (6.0 or newer) is needed to build the mod. Install it with:"
    Write-Host "  winget install Microsoft.DotNet.SDK.8"
    Write-Host "then open a new PowerShell window and run this script again."
    exit 1
}

# 2. Stardew Valley
Info "Looking for Stardew Valley"
if (-not $GamePath) { $GamePath = Find-Game }
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath "Stardew Valley.dll"))) {
    Fail "Couldn't find Stardew Valley. Pass its folder with -GamePath `"C:\path\to\Stardew Valley`"."
}
Write-Host "    $GamePath"

# 3. SMAPI
if (-not (Test-Path (Join-Path $GamePath "StardewModdingAPI.dll"))) {
    if ($InstallSmapi) { Install-Smapi $GamePath }
    else { Fail "SMAPI isn't installed. Re-run with -InstallSmapi, or install it from https://smapi.io." }
}

# 4. Build. ModBuildConfig copies the mod into <game>\Mods\AskTheAlmanac and keeps an existing config.json.
Info "Building and installing the mod"
& dotnet build AskTheAlmanac.csproj -c Release "-p:GamePath=$GamePath" -nologo -v quiet
if ($LASTEXITCODE -ne 0) { Fail "The build failed." }
Write-Host "    Installed to $GamePath\Mods\AskTheAlmanac"

# 5. Ollama
if (Get-Command ollama -ErrorAction SilentlyContinue) {
    Info "Ollama is installed. For the default cloud model, run 'ollama signin' once if you haven't."
}
else {
    Write-Host "Warning: Ollama isn't installed. The mod needs it to answer questions. Install it with:" -ForegroundColor Yellow
    Write-Host "  winget install Ollama.Ollama"
    Write-Host "then run 'ollama signin' for cloud models."
}

Info "Done! In Steam, set Stardew Valley's launch options to:"
Write-Host "    `"$GamePath\StardewModdingAPI.exe`" %command%"
Write-Host "    then launch the game, load a save and press F2."
