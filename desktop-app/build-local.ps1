#Requires -Version 5.1
<#
Build Bazino Marketing Studio and/or Bazino Studio Bridge on the owner's Windows PC.
No GitHub Actions, artifact upload, account token, or cloud build is involved.
Run with 64-bit Windows PowerShell 5.1 (powershell.exe), not pwsh on Linux.

Examples (from this directory):
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-local.ps1 -Target Studio -InstallPs2Exe
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-local.ps1 -Target All -InstallPs2Exe

-InstallPs2Exe explicitly allows installing the ps2exe module from PowerShell
Gallery for the current user if it is not already installed.
-Force allows replacing earlier files in dist\local; without it they are kept.
#>
[CmdletBinding()]
param(
    [ValidateSet('Studio', 'Bridge', 'All')]
    [string]$Target = 'Studio',
    [switch]$InstallPs2Exe,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required source file is missing: $Path"
    }
}

function Test-SourceSyntax([string]$Path) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $Path, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        throw "PowerShell syntax error in ${Path}: $($parseErrors[0].Message)"
    }
}

function Ensure-Ps2Exe {
    if (-not (Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue)) {
        if (-not (Get-Module -ListAvailable -Name ps2exe)) {
            if (-not $InstallPs2Exe) {
                throw 'ps2exe is missing. Rerun with -InstallPs2Exe to install it from PowerShell Gallery.'
            }
            if (-not (Get-Command Install-Module -ErrorAction SilentlyContinue)) {
                throw 'PowerShellGet/Install-Module is unavailable. Install ps2exe manually and rerun.'
            }
            Write-Host 'Installing ps2exe for the current Windows user from PowerShell Gallery...'
            Install-Module -Name ps2exe -Scope CurrentUser -Force -AllowClobber
        }
        Import-Module ps2exe -ErrorAction Stop
    }
    if (-not (Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue)) {
        throw 'ps2exe was imported, but Invoke-PS2EXE is unavailable.'
    }
}

function Invoke-Npm([string[]]$Arguments) {
    & $script:NpmCmd @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "npm $($Arguments[0]) failed with exit code $LASTEXITCODE"
    }
}

function Get-NodeToolchain {
    $node = (Get-Command node.exe -CommandType Application -ErrorAction Stop).Source
    $script:NpmCmd = (Get-Command npm.cmd -CommandType Application -ErrorAction Stop).Source
    $versionText = & $node -p 'process.versions.node'
    if ($LASTEXITCODE -ne 0) { throw 'Could not determine the Node.js version.' }
    if ([version]$versionText -lt [version]'22.12.0') {
        throw "Node.js 22.12.0 or newer is required; found $versionText"
    }
    $arch = & $node -p 'process.arch'
    if ($LASTEXITCODE -ne 0 -or $arch -ne 'x64') {
        throw 'The Studio archive requires x64 Node.js for Windows (node.exe).'
    }
    $nodeExe = & $node -p 'process.execPath'
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the active Node.js binary.' }
    Assert-File $nodeExe
    Write-Host "Using Node.js $versionText ($nodeExe)"
    return $nodeExe
}

function Test-SourceSelfTest([string]$Path, [string]$LogPath) {
    Remove-Item -LiteralPath $LogPath -Force -ErrorAction SilentlyContinue
    $previous = $env:BAZINO_SELFTEST
    try {
        $env:BAZINO_SELFTEST = '1'
        & (Join-Path $PSHOME 'powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $Path -SelfTest | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Source self-test failed: $Path ($LASTEXITCODE)"
        }
    } finally {
        if ($null -eq $previous) {
            Remove-Item Env:BAZINO_SELFTEST -ErrorAction SilentlyContinue
        } else {
            $env:BAZINO_SELFTEST = $previous
        }
    }
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf) -or
        (Get-Content -LiteralPath $LogPath -Raw) -notmatch 'self-test OK|SELFTEST OK') {
        throw "Source self-test did not write a success log: $Path"
    }
}

function Test-WindowsExe([string]$Path) {
    Assert-File $Path
    $size = (Get-Item -LiteralPath $Path).Length
    if ($size -lt 20480) { throw "EXE is suspiciously small ($size bytes): $Path" }
    $stream = [IO.File]::OpenRead($Path)
    try {
        if ($stream.ReadByte() -ne 0x4D -or $stream.ReadByte() -ne 0x5A) {
            throw "Missing Windows PE header: $Path"
        }
    } finally {
        $stream.Dispose()
    }
}

function Test-ExeSelfTest([string]$Path, [string]$LogPath) {
    Remove-Item -LiteralPath $LogPath -Force -ErrorAction SilentlyContinue
    $previous = $env:BAZINO_SELFTEST
    try {
        $env:BAZINO_SELFTEST = '1'
        $process = Start-Process -FilePath $Path -WorkingDirectory (Split-Path $Path -Parent) -PassThru
        try {
            if (-not $process.WaitForExit(45000)) {
                throw "Packaged EXE self-test timed out: $Path"
            }
            if ($process.ExitCode -ne 0) {
                throw "Packaged EXE self-test failed: $Path ($($process.ExitCode))"
            }
        } finally {
            if (-not $process.HasExited) {
                $process.Kill()
                [void]$process.WaitForExit(5000)
            }
            $process.Dispose()
        }
    } finally {
        if ($null -eq $previous) {
            Remove-Item Env:BAZINO_SELFTEST -ErrorAction SilentlyContinue
        } else {
            $env:BAZINO_SELFTEST = $previous
        }
    }
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf) -or
        (Get-Content -LiteralPath $LogPath -Raw) -notmatch 'self-test OK|SELFTEST OK') {
        throw "Packaged EXE did not write a success log: $Path"
    }
}

function Build-Studio([string]$WorkRoot, [string]$NodeExe) {
    Write-Host 'Testing Marketing Studio source...'
    Test-SourceSyntax $studioSource
    Test-SourceSelfTest $studioSource $studioLog
    Push-Location $appRoot
    try {
        Invoke-Npm -Arguments @('ci', '--ignore-scripts', '--no-audit', '--no-fund')
        Invoke-Npm -Arguments @('test')
        Invoke-Npm -Arguments @('run', 'check')
    } finally {
        Pop-Location
    }

    # An allowlisted payload: no relay state, test fixtures, tokens or agent client.
    $stage = Join-Path $WorkRoot 'studio'
    $stageApp = Join-Path $stage 'app'
    New-Item -ItemType Directory -Path $stageApp -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $appRoot 'src') -Destination (Join-Path $stageApp 'src') -Recurse
    Copy-Item -LiteralPath (Join-Path $appRoot 'package.json') -Destination $stageApp
    Copy-Item -LiteralPath (Join-Path $appRoot 'package-lock.json') -Destination $stageApp
    Push-Location $stageApp
    try {
        Invoke-Npm -Arguments @('ci', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund')
    } finally {
        Pop-Location
    }
    Assert-File (Join-Path $stageApp 'node_modules\@modelcontextprotocol\sdk\package.json')
    # The lockfile is needed for installation, not for the offline runtime.
    Remove-Item -LiteralPath (Join-Path $stageApp 'package-lock.json')
    $bundledNode = Join-Path $stage 'node.exe'
    Copy-Item -LiteralPath $NodeExe -Destination $bundledNode
    Test-WindowsExe $bundledNode

    $exe = Join-Path $stage 'BazinoMarketing.exe'
    Invoke-PS2EXE -InputFile $studioSource -OutputFile $exe -noConsole `
        -title 'Bazino Marketing Studio' -product 'Bazino Marketing Studio' `
        -description 'Local browser marketing app; no Electron' `
        -company 'Bazino' -version '0.2.0.0' | Out-Host
    Test-WindowsExe $exe
    Test-ExeSelfTest $exe $studioLog

    $zip = Join-Path $WorkRoot 'BazinoMarketing-Windows-x64.zip'
    Compress-Archive -LiteralPath @($exe, $bundledNode, $stageApp) `
        -DestinationPath $zip -CompressionLevel Optimal
    Assert-File $zip
    return $zip
}

function Build-Bridge([string]$WorkRoot) {
    Write-Host 'Testing Studio Bridge source...'
    Test-SourceSyntax $bridgeSource
    Test-SourceSelfTest $bridgeSource $bridgeLog
    $stage = Join-Path $WorkRoot 'bridge'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $exe = Join-Path $stage 'BazinoStudioBridge.exe'
    Invoke-PS2EXE -InputFile $bridgeSource -OutputFile $exe -noConsole `
        -title 'Bazino Studio Bridge' -product 'BazinoStudioBridge' `
        -description 'Short-lived Bazino Studio browser bridge for the Arena session branch' `
        -company 'Bazino' -version '1.6.0.0' | Out-Host
    Test-WindowsExe $exe
    Test-ExeSelfTest $exe $bridgeLog
    return $exe
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    $PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8) {
    throw 'Use 64-bit Windows PowerShell 5.1 (powershell.exe) on Windows to build these EXEs.'
}

$appRoot = $PSScriptRoot
$studioSource = Join-Path $appRoot 'BazinoMarketing.ps1'
$bridgeSource = Join-Path $appRoot 'browser-link\BazinoStudioBridge.ps1'
$studioLog = Join-Path ([IO.Path]::GetTempPath()) 'bazino-marketing-selftest.log'
$bridgeLog = Join-Path ([IO.Path]::GetTempPath()) 'bazino-studio-bridge-selftest.log'
$outputDir = Join-Path $appRoot 'dist\local'
$buildStudio = $Target -eq 'Studio' -or $Target -eq 'All'
$buildBridge = $Target -eq 'Bridge' -or $Target -eq 'All'
$studioOut = Join-Path $outputDir 'BazinoMarketing-Windows-x64.zip'
$bridgeOut = Join-Path $outputDir 'BazinoStudioBridge.exe'

if ($buildStudio) {
    foreach ($source in @($studioSource, (Join-Path $appRoot 'src\server.cjs'),
                            (Join-Path $appRoot 'src\server-address.cjs'),
                            (Join-Path $appRoot 'src\restart-existing.cjs'),
                            (Join-Path $appRoot 'package.json'), (Join-Path $appRoot 'package-lock.json'))) {
        Assert-File $source
    }
}
if ($buildBridge) { Assert-File $bridgeSource }
$outputs = @()
if ($buildStudio) { $outputs += $studioOut }
if ($buildBridge) { $outputs += $bridgeOut }
foreach ($output in $outputs) {
    if ((Test-Path -LiteralPath $output) -and -not $Force) {
        throw "Output already exists: $output. Keep it, or rerun with -Force to replace it."
    }
}

$nodeExe = $null
if ($buildStudio) { $nodeExe = Get-NodeToolchain }
Ensure-Ps2Exe

$work = Join-Path ([IO.Path]::GetTempPath()) ('BazinoLocalBuild-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
try {
    $built = @()
    if ($buildStudio) {
        $built += [pscustomobject]@{ Source = (Build-Studio $work $nodeExe); Output = $studioOut }
    }
    if ($buildBridge) {
        $built += [pscustomobject]@{ Source = (Build-Bridge $work); Output = $bridgeOut }
    }
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    foreach ($file in $built) {
        Copy-Item -LiteralPath $file.Source -Destination $file.Output -Force
        Write-Host "Built: $($file.Output)"
        Write-Host "SHA256: $((Get-FileHash -LiteralPath $file.Output -Algorithm SHA256).Hash)"
    }
    Write-Host 'Local build complete. No files were uploaded to GitHub.'
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
