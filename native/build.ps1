<#
Builds native\bin\win-<platform>\tree-sitter.dll: the tree-sitter runtime and the grammars FloorPlan
outlines files with, from the commits pinned in sources.json, compiled with the C++ build tools of
Visual Studio and the C runtime linked in. FloorPlan.Core's build runs it when the library is missing
or older than its inputs. Sources are fetched with Git, which checks them against their commits, into
native\obj\; nothing of theirs runs.

    powershell -NoProfile -ExecutionPolicy Bypass -File native\build.ps1 [-Platform x64|arm64]
#>
param([ValidateSet('x64', 'arm64')] [string] $Platform = 'x64')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$pins = Get-Content (Join-Path $here 'sources.json') -Raw | ConvertFrom-Json
$sources = Join-Path $here 'obj\sources'
$objects = Join-Path $here "obj\win-$Platform"
$output = Join-Path $here "bin\win-$Platform"

function Invoke-Tool([string] $tool, [string[]] $arguments) {
    & $tool @arguments
    if ($LASTEXITCODE -ne 0) { throw "$tool failed (exit code $LASTEXITCODE)" }
}

# A repository at its pinned commit, fetched once: Git refuses content that does not match the commit's hash.
function Get-Source($pin) {
    $name = ($pin.repository -split '/')[-1]
    $dir = Join-Path $sources ($name + '-' + $pin.commit.Substring(0, 12))
    $done = Join-Path $dir '.fetched'
    if ((Test-Path $done) -and ((Get-Content $done -Raw).Trim() -eq $pin.commit)) { return $dir }
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    Invoke-Tool git @('init', '-q', '--template=', $dir)
    Invoke-Tool git @('-C', $dir, 'fetch', '-q', '--depth', '1', '--no-tags', $pin.repository, $pin.commit)
    Invoke-Tool git @('-C', $dir, '-c', 'advice.detachedHead=false', '-c', 'core.autocrlf=false', 'checkout', '-q', 'FETCH_HEAD')
    $head = (& git -C $dir rev-parse HEAD).Trim()
    if ($head -ne $pin.commit) { throw "$($pin.repository): expected commit $($pin.commit), got $head" }
    Set-Content -Path $done -Value $pin.commit -Encoding ASCII
    return $dir
}

# The compiler's environment, as the Developer Command Prompt sets it.
function Import-BuildTools([string] $platform) {
    $component = if ($platform -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = $null
    if (Test-Path $vswhere) { $vs = & $vswhere -latest -products * -requires $component -property installationPath | Select-Object -First 1 }
    if (-not $vs) {
        throw "Building FloorPlan's parsers needs Visual Studio's C++ build tools ($component): install Build Tools for Visual Studio with the 'Desktop development with C++' workload."
    }
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
    $arch = if ($platform -eq 'arm64') { 'x64_arm64' } else { 'x64' }
    $variables = & cmd.exe /c "`"$vcvars`" $arch >nul && set"
    if ($LASTEXITCODE -ne 0) { throw "vcvarsall.bat $arch failed" }
    foreach ($line in $variables) {
        $equals = $line.IndexOf('=')
        if ($equals -gt 0) { [Environment]::SetEnvironmentVariable($line.Substring(0, $equals), $line.Substring($equals + 1), 'Process') }
    }
}

Import-BuildTools $Platform
New-Item -ItemType Directory -Force -Path $objects, $output | Out-Null

$compile = @('/nologo', '/c', '/O2', '/MT', '/std:c11', '/utf-8', '/W0', '/bigobj', '/DNDEBUG', '/guard:cf')
$objectFiles = New-Object System.Collections.Generic.List[string]
function Compile([string] $file, [string[]] $include, [string] $name) {
    $object = Join-Path $objects "$name.obj"
    $arguments = $compile + @($include | ForEach-Object { "/I$_" }) + @($file, "/Fo$object")
    Invoke-Tool cl.exe $arguments
    $objectFiles.Add($object)
}

$runtime = Get-Source $pins.runtime
Compile (Join-Path $runtime 'lib\src\lib.c') @((Join-Path $runtime 'lib\include'), (Join-Path $runtime 'lib\src')) 'runtime'

# The runtime's functions FloorPlan calls, then each grammar's entry point.
$exports = New-Object System.Collections.Generic.List[string]
$exports.AddRange([string[]](Get-Content (Join-Path $here 'tree-sitter.def')))
foreach ($grammar in $pins.grammars) {
    $folder = Join-Path (Get-Source $grammar) ($grammar.folder -replace '/', '\')
    Compile (Join-Path $folder 'parser.c') @($folder) "$($grammar.language)-parser"
    $scanner = Join-Path $folder 'scanner.c'
    if (Test-Path $scanner) { Compile $scanner @($folder) "$($grammar.language)-scanner" }
    $exports.Add("    tree_sitter_$($grammar.language)")
}
$def = Join-Path $objects 'tree-sitter.def'
Set-Content -Path $def -Value $exports -Encoding ASCII

$link = @('/nologo', '/DLL', "/OUT:$(Join-Path $output 'tree-sitter.dll')", "/DEF:$def", "/IMPLIB:$(Join-Path $objects 'tree-sitter.lib')",
    '/OPT:REF', '/OPT:ICF', '/guard:cf', '/DYNAMICBASE', '/NXCOMPAT')
if ($Platform -eq 'x64') { $link += '/CETCOMPAT' }
Invoke-Tool link.exe ($link + $objectFiles)
Write-Host "Built $(Join-Path $output 'tree-sitter.dll')"
