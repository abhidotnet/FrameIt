param(
    [switch]$Installer
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "FrameIt.sln"
$project = Join-Path $root "src/FrameIt/FrameIt.csproj"
$iss = Join-Path $root "installer/FrameIt-Beta.iss"

$portableOut = Join-Path $root "artifacts/portable"
$frameworkOut = Join-Path $root "artifacts/framework-dependent"
$distPortable = Join-Path $root "dist/portable"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

function Sync-DistPortable {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (Test-Path $Destination) {
        Get-ChildItem -Path $Destination -Force | Remove-Item -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
    Get-ChildItem -Path $Destination -Filter "*.pdb" -Recurse -File | Remove-Item -Force
}

function Find-Iscc {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $command = Get-Command "iscc" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($rootPath in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA)) {
        if ([string]::IsNullOrWhiteSpace($rootPath)) {
            continue
        }

        if ($rootPath -eq $env:LOCALAPPDATA) {
            $candidates.Add((Join-Path $rootPath "Programs\Inno Setup 6\ISCC.exe"))
        }
        else {
            $candidates.Add((Join-Path $rootPath "Inno Setup 6\ISCC.exe"))
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

Push-Location $root
try {
    dotnet restore $solution /p:EnableWindowsTargeting=true
    dotnet build $solution -c Release /p:EnableWindowsTargeting=true

    New-Item -ItemType Directory -Path $portableOut -Force | Out-Null
    New-Item -ItemType Directory -Path $frameworkOut -Force | Out-Null

    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:PublishReadyToRun=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableWindowsTargeting=true `
        -o $portableOut

    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained false `
        /p:PublishReadyToRun=true `
        /p:EnableWindowsTargeting=true `
        -o $frameworkOut

    Sync-DistPortable -Source $frameworkOut -Destination $distPortable

    if ($Installer) {
        $iscc = Find-Iscc
        if (-not $iscc) {
            throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6, then run ./build.ps1 -Installer. That writes dist/installer/FrameIt-Beta-Setup.exe from installer/FrameIt-Beta.iss."
        }

        & $iscc $iss
        if ($LASTEXITCODE -ne 0) {
            throw "ISCC.exe exited with code $LASTEXITCODE."
        }
    }
}
finally {
    Pop-Location
}
