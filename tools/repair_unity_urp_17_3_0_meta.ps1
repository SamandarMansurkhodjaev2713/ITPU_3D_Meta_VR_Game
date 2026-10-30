[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\unity\ITPU_VR_Unity')
)

$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$packageCache = Join-Path $resolvedProject 'Library\PackageCache'
$packages = @(Get-ChildItem -LiteralPath $packageCache -Directory -Filter 'com.unity.render-pipelines.universal@*' -ErrorAction Stop)
if ($packages.Count -ne 1) {
    throw "Expected exactly one cached URP package, found $($packages.Count) in $packageCache"
}

$packageJsonPath = Join-Path $packages[0].FullName 'package.json'
$packageJson = Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json
if ($packageJson.name -ne 'com.unity.render-pipelines.universal' -or $packageJson.version -ne '17.3.0') {
    throw "Refusing repair for unexpected package $($packageJson.name)@$($packageJson.version)"
}

$converterPath = Join-Path $packages[0].FullName 'Editor\Tools\Converters\ReadonlyMaterialConverter'
$repairs = @(
    @{
        Source = 'ReadonlyMaterialConverter.MaterialReferenceBuilder.cs'
        Meta = "fileFormatVersion: 2`nguid: 3abaee8973a0b7800923ad9dd0744dc0`n"
    },
    @{
        Source = 'ReadonlyMaterialConverter.MaterialReferenceChanger.cs'
        Meta = "fileFormatVersion: 2`nguid: 13c00535b19b330e375f1ef8cd9bc706`n"
    }
)

foreach ($repair in $repairs) {
    $sourcePath = Join-Path $converterPath $repair.Source
    $metaPath = "$sourcePath.meta"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "URP repair source is missing: $sourcePath"
    }
    if (Test-Path -LiteralPath $metaPath -PathType Leaf) {
        $existing = (Get-Content -LiteralPath $metaPath -Raw).Replace("`r`n", "`n")
        if ($existing -ne $repair.Meta) {
            throw "Refusing to overwrite non-matching metadata: $metaPath"
        }
        continue
    }
    [System.IO.File]::WriteAllText($metaPath, $repair.Meta, [System.Text.UTF8Encoding]::new($false))
}

[pscustomobject]@{
    status = 'PASS'
    package = "$($packageJson.name)@$($packageJson.version)"
    repaired = $repairs.Source
    package_cache = $packages[0].FullName
} | ConvertTo-Json -Depth 3
