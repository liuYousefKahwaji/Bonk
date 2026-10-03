param(
    [string]$Runtime = 'win-x64',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$bonkRoot = Split-Path $PSScriptRoot -Parent
$bonkProject = Join-Path $bonkRoot 'src\Bonk.App\Bonk.App.csproj'
[xml]$bonkProjectXml = Get-Content -LiteralPath $bonkProject
$bonkVersion = $bonkProjectXml.Project.PropertyGroup.Version
$bonkArtifactRoot = Join-Path $bonkRoot 'artifacts'
$bonkOutput = Join-Path $bonkArtifactRoot "portable-$Runtime"
$bonkArchive = Join-Path $bonkArtifactRoot "Bonk-$bonkVersion-$Runtime.zip"

& $Dotnet publish $bonkProject --configuration Release --runtime $Runtime --self-contained true -p:PublishSingleFile=false --output $bonkOutput
if ($LASTEXITCODE -ne 0) { throw 'Bonk publish failed.' }
Copy-Item -LiteralPath (Join-Path $bonkRoot 'README.md') -Destination $bonkOutput
Copy-Item -LiteralPath (Join-Path $bonkRoot 'THIRD-PARTY-NOTICES.md') -Destination $bonkOutput
Copy-Item -LiteralPath (Join-Path $bonkRoot 'licenses') -Destination $bonkOutput -Recurse -Force
$bonkNuGetDetails = & $Dotnet nuget locals global-packages --list
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the runtime license files.' }
$bonkNuGetRoot = ($bonkNuGetDetails -split ': ', 2)[1].Trim()
$bonkRuntimeConfig = Get-Content -LiteralPath (Join-Path $bonkOutput 'Bonk.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($bonkFramework in $bonkRuntimeConfig.runtimeOptions.includedFrameworks) {
    $bonkPackageName = "$($bonkFramework.name.ToLowerInvariant()).runtime.$Runtime"
    $bonkPackageRoot = Join-Path (Join-Path $bonkNuGetRoot $bonkPackageName) $bonkFramework.version
    foreach ($bonkNoticeName in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        $bonkNoticePath = Join-Path $bonkPackageRoot $bonkNoticeName
        if (Test-Path -LiteralPath $bonkNoticePath) {
            Copy-Item -LiteralPath $bonkNoticePath -Destination (Join-Path $bonkOutput "licenses\$bonkPackageName-$bonkNoticeName")
        }
    }
}
Compress-Archive -Path (Join-Path $bonkOutput '*') -DestinationPath $bonkArchive -Force
Write-Output "Portable Bonk: $bonkArchive"
