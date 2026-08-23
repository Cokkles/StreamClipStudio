param(
    [switch]$Portable,
    [string]$Version = '0.6.0'
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'StreamClipStudio.csproj'
$releaseRoot = Join-Path $PSScriptRoot 'release'

if ($Portable) {
    $outputFolder = Join-Path $releaseRoot "v$Version-portable-win-x64"
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $outputFolder
}
else {
    $outputFolder = Join-Path $releaseRoot "v$Version-win-x64"
    dotnet publish $projectPath -c Release --self-contained false `
        -o $outputFolder
}

Write-Host "Release created in $outputFolder"
