param(
    [string]$SPTRoot = 'C:\SPTV',
    [switch]$NoBuild,
    [switch]$UseInstalledAssets
)
$ErrorActionPreference = 'Stop'
if (!$NoBuild) {
    dotnet build "$PSScriptRoot\ISBSpecialForces.slnx" -c Release "-p:SPTRoot=$SPTRoot" -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. No package was created.' }
}

$projectMetadata = [xml](Get-Content -LiteralPath "$PSScriptRoot\Plugin\Plugin.csproj" -Raw)
$version = @($projectMetadata.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid mod release version.' }
$releaseName = "ISB-SpecialForces-v$version-SPT-4.1.6-" + (Get-Date -Format 'yyyyMMdd-HHmmss')
$release = Join-Path $PSScriptRoot ('dist\' + $releaseName)
$plugins = Join-Path $release 'BepInEx\plugins\ISBSpecialForcesPlugin'
$patcher = Join-Path $release 'SPT_Runtime\user\patchers\com.samc137.isbspecialforces'
$server = Join-Path $release 'SPT_Runtime\user\mods\ISB-SpecialForces'
foreach ($directory in $plugins,$patcher,$server) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$clientBinaries = @(
    'Plugin\bin\Release\net472\ISB-SpecialForces.dll',
    'ISBSOFExtras\bin\Release\ISBSOF_Extras.dll',
    'ISBSOFExtras\bin\Release\ISBSOF_Extras.Fika.dll',
    'ISBNotify\bin\Release\ISBNotify.dll',
    'ISBNotify\Fika\bin\Release\ISBNotify.Fika.dll'
)
foreach ($binary in $clientBinaries) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $binary) -Destination $plugins }
Copy-Item -LiteralPath "$PSScriptRoot\Server\EnumExtensions\ISBEnums.json" -Destination $patcher
Copy-Item -LiteralPath "$PSScriptRoot\Server\bin\Release\ISBSpecialForces\ISBSpecialForces.dll" -Destination $server
Copy-Item -LiteralPath "$PSScriptRoot\Server\config.jsonc" -Destination $server
Copy-Item -LiteralPath "$PSScriptRoot\Server\containment_breach_config.jsonc" -Destination $server
foreach ($folder in 'db','sain_defaults') {
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\Server" $folder) -Destination $server -Recurse
}
foreach ($folder in 'Blackout','ISBSpecialForces_Audios','ISBSpecialForces_Videos','Office') {
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\ISBSOFExtras" $folder) -Destination $plugins -Recurse
}
if ($UseInstalledAssets) {
    $assetPairs = @(
        @("$SPTRoot\SPT_Runtime\user\mods\ISB-SpecialForces\db", "$server\db"),
        @("$SPTRoot\SPT_Runtime\user\mods\ISB-SpecialForces\sain_defaults", "$server\sain_defaults"),
        @("$SPTRoot\BepInEx\plugins\ISBSpecialForcesPlugin\Blackout", "$plugins\Blackout"),
        @("$SPTRoot\BepInEx\plugins\ISBSpecialForcesPlugin\ISBSpecialForces_Audios", "$plugins\ISBSpecialForces_Audios"),
        @("$SPTRoot\BepInEx\plugins\ISBSpecialForcesPlugin\ISBSpecialForces_Videos", "$plugins\ISBSpecialForces_Videos"),
        @("$SPTRoot\BepInEx\plugins\ISBSpecialForcesPlugin\Office", "$plugins\Office")
    )
    foreach ($pair in $assetPairs) {
        $sourceRoot = [IO.Path]::GetFullPath($pair[0]).TrimEnd('\')
        if (!(Test-Path -LiteralPath $sourceRoot -PathType Container)) { throw "Installed assets missing: $sourceRoot" }
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
            $destination = Join-Path $pair[1] $relative
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    Copy-Item -LiteralPath "$SPTRoot\SPT_Runtime\user\mods\ISB-SpecialForces\config.jsonc" -Destination "$server\config.jsonc" -Force
    Copy-Item -LiteralPath "$SPTRoot\SPT_Runtime\user\mods\ISB-SpecialForces\containment_breach_config.jsonc" -Destination "$server\containment_breach_config.jsonc" -Force
}
$manifest = Get-ChildItem $release -Recurse -File | ForEach-Object {
    [ordered]@{
        path = [IO.Path]::GetRelativePath($release, $_.FullName)
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$release\manifest.json" -Encoding utf8
[IO.Compression.ZipFile]::CreateFromDirectory($release, "$release.zip")
Write-Output "Package: $release.zip"


