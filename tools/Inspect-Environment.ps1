<#
Read-only Windows inventory. Writes only reports/local/environment.json.
No install, network, administrator mode, policy changes, serial numbers or account tokens.
Run only after explicit P0/preflight approval. This script was NOT executed on Windows
by the authoring assistant; test it on the real machine after approval.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$State = Get-Content -LiteralPath (Join-Path $Root 'state/project_state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$Approvals = Get-Content -LiteralPath (Join-Path $Root 'state/approvals.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($State.phase -ne 'P0' -or
    $Approvals.package.status -ne 'APPROVED' -or
    $Approvals.package.version -ne $State.package_version -or
    $State.owner_approved_package_version -ne $State.package_version -or
    [string]::IsNullOrWhiteSpace([string]$Approvals.package.evidence) -or
    $Approvals.preflight.status -ne 'APPROVED' -or
    [string]::IsNullOrWhiteSpace([string]$Approvals.preflight.evidence)) {
    throw 'DENY: explicit owner approval for this package and P0 preflight is required.'
}
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required.' }
$ReportDir = Join-Path $Root 'reports/local'
# Do not write through a pre-existing junction/symbolic link.
foreach ($p in @((Join-Path $Root 'reports'), $ReportDir)) {
    if (Test-Path -LiteralPath $p) {
        $item = Get-Item -LiteralPath $p
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Refusing to write through a reparse point in report path.'
        }
    }
}
$Cpu = @(Get-CimInstance -ClassName Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
$Gpu = @(Get-CimInstance -ClassName Win32_VideoController | Select-Object Name, DriverVersion)
$Os = Get-CimInstance -ClassName Win32_OperatingSystem
$Disks = @(Get-CimInstance -ClassName Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {
    [ordered]@{ drive = $_.DeviceID; size_gb = [math]::Round($_.Size / 1GB, 1); free_gb = [math]::Round($_.FreeSpace / 1GB, 1) }
})
$Commands = @()
foreach ($name in @('python', 'py', 'git', 'blender', 'Unity', 'adb', 'uv', 'uvx', 'codex')) {
    # Find executable paths ONLY. Never run an unknown executable from PATH here.
    $found = @(Get-Command -Name $name -CommandType Application -ErrorAction SilentlyContinue)
    $Commands += [ordered]@{ name = $name; found = ($found.Count -gt 0); paths = @($found | ForEach-Object { $_.Source }); runtime_test = 'NOT_RUN' }
}
$Editors = @()
$HubEditors = Join-Path $env:ProgramFiles 'Unity/Hub/Editor'
if (Test-Path -LiteralPath $HubEditors) {
    $Editors = @(Get-ChildItem -LiteralPath $HubEditors -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
}
$BlenderPaths = @()
$BlenderBase = Join-Path $env:ProgramFiles 'Blender Foundation'
if (Test-Path -LiteralPath $BlenderBase) {
    $BlenderPaths = @(Get-ChildItem -LiteralPath $BlenderBase -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
}
$Report = [ordered]@{
    generated_utc = [DateTime]::UtcNow.ToString('o')
    package_version = $State.package_version
    status = 'READ_ONLY_INVENTORY_NOT_COMPATIBILITY_TEST'
    os = [ordered]@{ caption = $Os.Caption; version = $Os.Version; build = $Os.BuildNumber }
    ram_gb = [math]::Round(($Os.TotalVisibleMemorySize * 1KB) / 1GB, 1)
    cpu = $Cpu
    gpu = $Gpu
    vram_note = 'VRAM capacity is intentionally omitted: Win32 adapter values can be unreliable. Verify in the GPU vendor/system interface.'
    disks = $Disks
    executables_on_path = $Commands
    unity_editor_folders = $Editors
    blender_folders = $BlenderPaths
    powershell_version = $PSVersionTable.PSVersion.ToString()
    not_checked = @('Unity license eligibility', 'SDK compatibility', 'MCP trust and connection', 'Quest device permissions', 'VR performance', 'Codex app capabilities')
    privacy = 'Local executable paths may contain a Windows account name. Review/redact this LOCAL file before sending it to chat or cloud services.'
}
New-Item -ItemType Directory -Path $ReportDir -Force | Out-Null
$Output = Join-Path $ReportDir 'environment.json'
$Report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding UTF8
Write-Output 'Inventory complete. Local report: reports/local/environment.json. Review/redact before sharing.'
