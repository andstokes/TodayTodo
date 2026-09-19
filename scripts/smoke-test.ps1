[CmdletBinding()]
param([string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = [IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
. (Join-Path $scriptDirectory 'release-common.ps1')

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot 'artifacts\publish\win-x64\TodayChecklist.exe'
}
$executable = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw '找不到发布版 TodayChecklist.exe；请先发布，或传入 -ExecutablePath。'
}

$runRoot = New-ReleaseWorkspace $projectRoot 'smoke-tests'
$temporaryRoot = Join-Path $runRoot 'temp'
$workingDirectory = Join-Path $runRoot '独立 工作目录'
[void][IO.Directory]::CreateDirectory($temporaryRoot)
[void][IO.Directory]::CreateDirectory($workingDirectory)

$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executable
$start.Arguments = '--test-mode --smoke-test'
$start.WorkingDirectory = $workingDirectory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables['TEMP'] = $temporaryRoot
$start.EnvironmentVariables['TMP'] = $temporaryRoot
$start.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
$start.EnvironmentVariables['TESTINGPLATFORM_TELEMETRY_OPTOUT'] = '1'

$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    if (-not $process.Start()) { throw '无法启动隔离测试进程。' }
    Wait-ReleaseProcess $process $executable
    $testBase = Join-Path $temporaryRoot 'TodayChecklist-Test'
    if (-not (Test-Path -LiteralPath $testBase -PathType Container)) {
        throw '测试进程结束，但本次会话目录不存在。'
    }
    $sessions = @(Get-ChildItem -LiteralPath $testBase -Directory -Force)
    if ($sessions.Count -ne 1) { throw '本次测试应只产生一个隔离会话。' }
    $dataFile = Join-Path $sessions[0].FullName 'LocalAppData\TodayChecklist\data\tasks.json'
    if (-not (Test-Path -LiteralPath $dataFile -PathType Leaf)) {
        throw '本次隔离会话的任务数据不存在。'
    }
    $data = Get-Content -LiteralPath $dataFile -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($data.schemaVersion -ne 1 -or @($data.tasks).Count -eq 0) {
        throw '本次隔离数据没有有效的测试任务。'
    }
    Write-Host '隔离 WPF 启动烟雾测试通过。'
    Write-Host "隔离测试位置：$runRoot"
    [pscustomobject]@{
        executable = $executable
        sessionDirectory = $sessions[0].FullName
        workingDirectory = $workingDirectory
        exitCode = 0
    }
}
finally {
    $process.Dispose()
}
