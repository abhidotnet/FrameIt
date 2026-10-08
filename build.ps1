param(
    [switch]$Installer,
    [switch]$Msix,
    [switch]$MsixSideload,
    [string]$MsixPublisher = "",
    [switch]$UpdateDist
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

function Read-FrameItPackageProps {
    $path = Join-Path $root "packaging/msix/Package.props"
    $text = Get-Content -Raw -LiteralPath $path

    function Read-Prop([string]$name) {
        $match = [regex]::Match($text, "<$name>([^<]+)</$name>")
        if (-not $match.Success) {
            throw "packaging/msix/Package.props is missing <$name>."
        }

        return $match.Groups[1].Value.Trim()
    }

    return [pscustomobject]@{
        Version = Read-Prop "Version"
        IdentityName = Read-Prop "FrameItIdentityName"
        Publisher = Read-Prop "FrameItPublisher"
        PublisherDisplayName = Read-Prop "FrameItPublisherDisplayName"
        DisplayName = Read-Prop "FrameItDisplayName"
        SideloadPublisher = Read-Prop "FrameItSideloadPublisher"
    }
}

function Convert-ToXmlAttribute([string]$value) {
    return $value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")
}

function Find-MakeAppx {
    $command = Get-Command "makeappx.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $command = Get-Command "MakeAppx.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($rootPath in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ([string]::IsNullOrWhiteSpace($rootPath)) {
            continue
        }

        $kit = Join-Path $rootPath "Windows Kits\10\bin"
        if (Test-Path -LiteralPath $kit) {
            $roots.Add($kit)
        }
    }

    $found = @()
    foreach ($kit in $roots) {
        $found += Get-ChildItem -Path $kit -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "[\\/]x64[\\/]makeappx.exe$" }
    }

    $best = $found | Sort-Object FullName -Descending | Select-Object -First 1
    if ($best) {
        return $best.FullName
    }

    return $null
}

function New-MsixPackage {
    param(
        [string]$PublisherOverride,
        [switch]$Sideload
    )

    $props = Read-FrameItPackageProps
    $publisher = $props.Publisher
    if ($Sideload) {
        $publisher = $props.SideloadPublisher
    }

    if (-not [string]::IsNullOrWhiteSpace($PublisherOverride)) {
        $publisher = $PublisherOverride.Trim()
    }

    $makeAppx = Find-MakeAppx
    if (-not $makeAppx) {
        throw "Windows SDK MakeAppx (makeappx.exe) was not found. Install the Windows 10 SDK or Windows 11 SDK, then run ./build.ps1 -Msix. That writes artifacts/msix/FrameIt_<version>_x64.msix and does not write to dist/."
    }

    $staging = Join-Path $root "artifacts/msix/publish"
    $layout = Join-Path $root "artifacts/msix/layout"
    $outputDir = Join-Path $root "artifacts/msix"
    if (Test-Path $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    if (Test-Path $layout) {
        Remove-Item -LiteralPath $layout -Recurse -Force
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

    # A normal self-contained folder, not a single file. MSIX installs the folder as the package root
    # and the manifest points at FrameIt.exe. The .NET 8 runtime is inside the package.
    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishSingleFile=false `
        /p:PublishReadyToRun=false `
        /p:Version=$($props.Version) `
        /p:AssemblyVersion=$($props.Version) `
        /p:FileVersion=$($props.Version) `
        /p:EnableWindowsTargeting=true `
        -o $staging

    New-Item -ItemType Directory -Path $layout -Force | Out-Null
    Copy-Item -Path (Join-Path $staging "*") -Destination $layout -Recurse -Force
    Get-ChildItem -Path $layout -Filter "*.pdb" -Recurse -File | Remove-Item -Force

    $assetDest = Join-Path $layout "Assets"
    New-Item -ItemType Directory -Path $assetDest -Force | Out-Null
    Copy-Item -Path (Join-Path $root "packaging/msix/Assets/*") -Destination $assetDest -Force

    $manifest = Get-Content -Raw -LiteralPath (Join-Path $root "packaging/msix/AppxManifest.xml")
    $manifest = $manifest.Replace("__IDENTITY_NAME__", (Convert-ToXmlAttribute $props.IdentityName))
    $manifest = $manifest.Replace("__PUBLISHER__", (Convert-ToXmlAttribute $publisher))
    $manifest = $manifest.Replace("__PUBLISHER_DISPLAY_NAME__", (Convert-ToXmlAttribute $props.PublisherDisplayName))
    $manifest = $manifest.Replace("__DISPLAY_NAME__", (Convert-ToXmlAttribute $props.DisplayName))
    $manifest = $manifest.Replace("__VERSION__", (Convert-ToXmlAttribute $props.Version))
    if ($manifest.Contains("__")) {
        throw "AppxManifest.xml still has unfilled tokens."
    }

    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $layout "AppxManifest.xml"), $manifest.TrimStart([char]0xFEFF), $utf8)

    $suffix = ""
    if ($Sideload -or $publisher -eq $props.SideloadPublisher) {
        $suffix = "_sideload"
    }

    $packageName = "FrameIt_$($props.Version)_x64$suffix.msix"
    $packagePath = Join-Path $outputDir $packageName
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }

    & $makeAppx pack /d $layout /p $packagePath /o
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx.exe exited with code $LASTEXITCODE."
    }

    Write-Host "MSIX: $packagePath"
    Write-Host "Publisher: $publisher"
    if ($suffix -eq "_sideload") {
        Write-Host "Sign on Windows with the test certificate (private key stays in the cert store):"
        Write-Host "signtool sign /sha1 EACD61BACD1A4D608F325F5F9E39EF8D3A9F9503 /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `"$packagePath`""
    }
}

Push-Location $root
try {
    if ($Msix -or $MsixSideload -or -not [string]::IsNullOrWhiteSpace($MsixPublisher)) {
        if (-not (Find-MakeAppx)) {
            throw "Windows SDK MakeAppx (makeappx.exe) was not found. Install the Windows 10 SDK or Windows 11 SDK, then run ./build.ps1 -Msix. That writes artifacts/msix/FrameIt_<version>_x64.msix and does not write to dist/."
        }
    }

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

    if ($UpdateDist) {
        Write-Host ""
        Write-Host "WARNING: -UpdateDist replaces the test-signed files in dist/portable."
        Write-Host "FrameIt.exe, FrameIt.dll, and FrameIt.deps.json will no longer match dist/FrameIt-Test-Certificate.cer."
        Write-Host "Sign them again before you commit or share dist/."
        Write-Host ""
        Sync-DistPortable -Source $frameworkOut -Destination $distPortable
    }

    if ($Installer) {
        $iscc = Find-Iscc
        if (-not $iscc) {
            throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6, then run ./build.ps1 -Installer. That writes dist/installer/FrameIt-Beta-Setup.exe from installer/FrameIt-Beta.iss. It does not modify dist/portable."
        }

        Write-Host ""
        Write-Host "WARNING: -Installer replaces dist/installer/FrameIt-Beta-Setup.exe."
        Write-Host "The setup exe in git is test-signed. The new file is unsigned and must be signed again before you commit or share it."
        Write-Host "dist/portable is left as it is."
        Write-Host ""
        & $iscc $iss
        if ($LASTEXITCODE -ne 0) {
            throw "ISCC.exe exited with code $LASTEXITCODE."
        }
    }

    if ($Msix -or $MsixSideload -or -not [string]::IsNullOrWhiteSpace($MsixPublisher)) {
        New-MsixPackage -PublisherOverride $MsixPublisher -Sideload:$MsixSideload
    }
}
finally {
    Pop-Location
}
