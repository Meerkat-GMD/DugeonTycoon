[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('doctor', 'test')]
    [string] $Command
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $root 'DotNet/DungeonTycoon.Core.Tests/DungeonTycoon.Core.Tests.csproj'

Push-Location -LiteralPath $root
try {
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($Command -eq 'test') {
        if (-not $dotnet) { throw 'Install the .NET SDK and add dotnet to PATH to run the Core tests.' }
        & $dotnet.Source test $testProject
        exit $LASTEXITCODE
    }

    $issues = @()
    $framework = ([xml](Get-Content -LiteralPath $testProject -Raw)).Project.PropertyGroup.TargetFramework
    $runtimeVersion = [version]($framework -replace '^net', '')
    if ($dotnet) {
        $sdk = & $dotnet.Source --version
        if ($LASTEXITCODE -eq 0 -and ($sdk -match '^(\d+)\.') -and [int]$Matches[1] -ge $runtimeVersion.Major) {
            Write-Output ".NET SDK $sdk"
        } else {
            $issues += "Install .NET SDK $runtimeVersion or later and check dotnet --version."
        }
        $runtimes = @(& $dotnet.Source --list-runtimes)
        $runtime = $runtimes -match ('^Microsoft\.NETCore\.App ' + [regex]::Escape("$runtimeVersion") + '\.\d+ ')
        if ($runtime) {
            Write-Output $runtime
        } else {
            $issues += "Install the Microsoft.NETCore.App $runtimeVersion runtime to run the Core tests."
        }
    } else {
        $issues += "Install .NET SDK $runtimeVersion or later and the .NET $runtimeVersion runtime, then add dotnet to PATH."
    }

    $versionLine = Select-String -LiteralPath 'ProjectSettings/ProjectVersion.txt' -Pattern '^m_EditorVersion:\s*(\S+)'
    $unityVersion = $versionLine.Matches[0].Groups[1].Value
    $editor = $env:UNITY_EDITOR_PATH
    if (-not $editor -and $env:ProgramFiles) {
        $editor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$unityVersion/Editor/Unity.exe"
    }
    if ($editor -and (Test-Path -LiteralPath $editor -PathType Leaf)) {
        $installedVersion = (Get-Item -LiteralPath $editor).VersionInfo.ProductVersion -split '_', 2
        if ($installedVersion[0] -eq $unityVersion) {
            Write-Output "Unity $unityVersion ($editor)"
        } else {
            $issues += "Expected Unity $unityVersion at '$editor'. Set UNITY_EDITOR_PATH to the matching Unity.exe."
        }
    } else {
        $issues += "Install Unity $unityVersion through Unity Hub or set UNITY_EDITOR_PATH to its Unity.exe."
    }

    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) {
        & $git.Source --version
        if ($LASTEXITCODE -ne 0) { $issues += 'Check git --version so Unity can restore UniTask.' }
    } else {
        $issues += 'Install Git and add it to PATH so Unity can restore UniTask.'
    }
    if ($issues) {
        foreach ($issue in $issues) { [Console]::Error.WriteLine($issue) }
        exit 1
    }
} finally {
    Pop-Location
}
