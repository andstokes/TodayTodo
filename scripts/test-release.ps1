[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = [IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
. (Join-Path $scriptDirectory 'release-common.ps1')
$testRoot = New-ReleaseWorkspace $projectRoot 'release-tests'
$script:releaseCheckCount = 0

function Assert-ReleaseTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "发布脚本回归测试失败：$Message" }
    $script:releaseCheckCount++
}
function Assert-ReleaseFailure {
    param([scriptblock]$Action, [string]$Pattern)
    $caught = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw }
        $caught = $true
    }
    Assert-ReleaseTest $caught "应拒绝：$Pattern"
}
function New-PortableFixture {
    param([string]$Path)
    [void][IO.Directory]::CreateDirectory($Path)
    foreach ($file in @('TodayChecklist.exe', 'TodayChecklist.dll', 'TodayChecklist.deps.json',
        'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'PresentationFramework.dll', 'System.Private.CoreLib.dll')) {
        Write-ReleaseTextFile (Join-Path $Path $file) 'fixture'
    }
    Write-ReleaseTextFile (Join-Path $Path 'TodayChecklist.runtimeconfig.json') (
        '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.11"},' +
        '{"name":"Microsoft.WindowsDesktop.App","version":"10.0.11"}]}}')
}
function Start-ReleaseTestSleeper {
    param([string]$ShellPath, [string]$WorkingDirectory)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $ShellPath
    $start.Arguments = '-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 20"'
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.EnvironmentVariables['TEMP'] = $WorkingDirectory
    $start.EnvironmentVariables['TMP'] = $WorkingDirectory
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw '无法启动回归测试辅助进程。' }
    return $process
}

Assert-ReleaseFailure { Assert-ReleaseChildPath $testRoot $projectRoot } '超出'
Assert-ReleaseFailure { Assert-ReleaseChildPath $testRoot $testRoot } '超出'
Assert-ReleaseFailure { Assert-ReleaseChildPath $testRoot (Join-Path $testRoot '..\outside') } '超出'
Assert-ReleaseFailure { Find-CachedReleasePackage @($testRoot) 'missing/1.0.0' } '缺少已缓存'
$noAssets = Join-Path $testRoot 'missing-assets\App.csproj'
Assert-ReleaseFailure { Assert-CachedReleaseAssets $noAssets } '缺少已有依赖清单'

$source = Join-Path $testRoot '原始 文件'
[void][IO.Directory]::CreateDirectory($source)
Write-ReleaseTextFile (Join-Path $source '中文 file.txt') 'ABC'
$manifest = @(Get-ReleaseInventory $source)
Assert-ReleaseInventory $manifest $source
Assert-ReleaseTest ($manifest.Count -eq 1) '生成文件清单'
Assert-ReleaseFailure { Write-ReleaseTextFile (Join-Path $source '中文 file.txt') 'XYZ' } '不会覆盖'
Assert-ReleaseInventory $manifest $source

$changed = Join-Path $testRoot 'changed'
[void][IO.Directory]::CreateDirectory($changed)
Write-ReleaseTextFile (Join-Path $changed '中文 file.txt') 'XYZ'
Assert-ReleaseFailure { Assert-ReleaseInventory $manifest $changed } '内容校验'
$extra = Join-Path $testRoot 'extra'
[void][IO.Directory]::CreateDirectory($extra)
Write-ReleaseTextFile (Join-Path $extra '中文 file.txt') 'ABC'
Write-ReleaseTextFile (Join-Path $extra 'extra.txt') 'extra'
Assert-ReleaseFailure { Assert-ReleaseInventory $manifest $extra } '数量校验'
$renamed = Join-Path $testRoot 'renamed'
[void][IO.Directory]::CreateDirectory($renamed)
Write-ReleaseTextFile (Join-Path $renamed 'different.txt') 'ABC'
Assert-ReleaseFailure { Assert-ReleaseInventory $manifest $renamed } '路径校验'

$fixture = Join-Path $testRoot 'payload'
New-PortableFixture $fixture
Assert-PortablePayload $fixture
Assert-ReleaseTest $true '自包含清单'
Write-ReleaseTextFile (Join-Path $fixture 'tasks.json') '{"synthetic":true}'
Assert-ReleaseFailure { Assert-PortablePayload $fixture } '不应分发'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $testRoot 'roundtrip.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)
$roundtrip = Join-Path $testRoot '解压 验收'
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $roundtrip)
Assert-ReleaseInventory $manifest $roundtrip
Assert-ReleaseTest $true 'ZIP 往返'

$first = Join-Path $testRoot 'first-candidate'
[void][IO.Directory]::CreateDirectory($first)
Write-ReleaseTextFile (Join-Path $first 'marker.txt') 'original'
$final = Join-Path $testRoot 'releases\1.0.0'
Complete-ReleaseDirectory $projectRoot $first $final
Assert-ReleaseTest (-not (Test-Path -LiteralPath $first)) '提交候选目录'
$finalManifest = @(Get-ReleaseInventory $final)
$second = Join-Path $testRoot 'second-candidate'
[void][IO.Directory]::CreateDirectory($second)
Write-ReleaseTextFile (Join-Path $second 'marker.txt') 'replacement'
Assert-ReleaseFailure { Complete-ReleaseDirectory $projectRoot $second $final } '不会覆盖'
Assert-ReleaseInventory $finalManifest $final
Assert-ReleaseTest (Test-Path -LiteralPath $second) '保留被拒绝的候选目录'

$shellExecutable = (Get-Process -Id $PID).Path
foreach ($label in @('Release 构建', '全部自动测试', '自包含发布')) {
    $guard = Join-Path $testRoot ('should-not-commit-' + [Guid]::NewGuid().ToString('N'))
    Assert-ReleaseFailure {
        Invoke-ReleaseDotnet $shellExecutable @('-NoProfile', '-NonInteractive', '-Command', 'exit 7') $label
        [void][IO.Directory]::CreateDirectory($guard)
    } '退出代码：7'
    Assert-ReleaseTest (-not (Test-Path -LiteralPath $guard)) '失败后不能提交发布目录'
}

$beforeTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
$owned = Start-ReleaseTestSleeper $shellExecutable $testRoot
$peer = Start-ReleaseTestSleeper $shellExecutable $testRoot
try {
    Assert-ReleaseFailure { Wait-ReleaseProcess $owned $shellExecutable 250 } '超时'
    Assert-ReleaseTest $owned.HasExited '结束本次超时进程'
    Assert-ReleaseTest (-not $peer.HasExited) '保留同名的其他进程'
    Assert-ReleaseTest ([Environment]::GetEnvironmentVariable('TEMP', 'Process') -ceq $beforeTemp) '子进程临时目录不污染父进程'
}
finally {
    foreach ($child in @($owned, $peer)) {
        if (-not $child.HasExited -and
            $child.MainModule.FileName.Equals($shellExecutable, [StringComparison]::OrdinalIgnoreCase)) {
            $child.Kill()
            [void]$child.WaitForExit(5000)
        }
        $child.Dispose()
    }
}
Write-Host "发布脚本回归测试通过：$script:releaseCheckCount 项。"
