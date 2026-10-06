$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "FrameIt.sln"
$project = Join-Path $root "src/FrameIt/FrameIt.csproj"

$portableOut = Join-Path $root "artifacts/portable"
$frameworkOut = Join-Path $root "artifacts/framework-dependent"

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
}
finally {
    Pop-Location
}
