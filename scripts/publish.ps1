[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = [IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
. (Join-Path $scriptDirectory 'release-common.ps1')
$projectFile = Join-Path $projectRoot 'src\TodayChecklist\TodayChecklist.csproj'
$testProject = Join-Path $projectRoot 'tests\TodayChecklist.Tests\TodayChecklist.Tests.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本必须是明确的三段数字版本。' }
$destination = Assert-ReleaseChildPath $projectRoot (Join-Path $projectRoot "artifacts\releases\$version")
if (Test-Path -LiteralPath $destination) { throw "该版本已存在，不会覆盖：$destination" }

$runRoot = New-ReleaseWorkspace $projectRoot 'release-work'
$payload = Join-Path $runRoot 'package\TodayChecklist'
$candidate = Join-Path $runRoot 'candidate'
$verification = Join-Path $runRoot '解压 验收'
$temporaryRoot = Join-Path $runRoot 'temp'
$emptyFeed = Join-Path $runRoot 'empty-feed'
foreach ($directory in @($payload, $candidate, $verification, $temporaryRoot, $emptyFeed)) {
    [void][IO.Directory]::CreateDirectory($directory)
}
$settings = @{
    TEMP = $temporaryRoot
    TMP = $temporaryRoot
    DOTNET_CLI_HOME = (Join-Path $runRoot 'dotnet-home')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    DOTNET_CLI_UI_LANGUAGE = 'en-US'
    MSBUILDDISABLENODEREUSE = '1'
}
$savedEnvironment = @{}
foreach ($name in $settings.Keys) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
}
Push-Location $projectRoot
try {
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $sdkVersion = (& $dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'global.json 要求的 .NET SDK 不可用；不会自动安装。' }
    $assets = Assert-CachedReleaseAssets $projectFile
    $testAssets = Assert-CachedReleaseAssets $testProject
    $testXml = [xml](Get-Content -LiteralPath $testProject -Raw -Encoding UTF8)
    $testSdk = [string]$testXml.Project.Sdk
    if ($testSdk -notmatch '^MSTest[.]Sdk/([0-9.]+)$') { throw '无法识别现有 MSTest SDK 版本。' }
    [void](Find-CachedReleasePackage @($testAssets.packageFolders.PSObject.Properties.Name) ("mstest.sdk/" + $Matches[1]))

    # All commands use cached assets only. Even an SDK resolver sees only an empty local feed.
    $offlineConfig = Join-Path $runRoot 'NuGet.offline.config'
    $escapedFeed = [Security.SecurityElement]::Escape($emptyFeed)
    Write-ReleaseTextFile $offlineConfig (
        '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="offline-empty" value="' +
        $escapedFeed + '"/></packageSources><auditSources><clear/></auditSources></configuration>')
    $buildOptions = @(
        '-c', 'Release', '--no-restore', '--disable-build-servers', '-warnaserror',
        '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
        '-p:EnableRuntimePackDownload=false', '-p:EnableTargetingPackDownload=false',
        "-p:RestoreSources=$emptyFeed", "-p:RestoreConfigFile=$offlineConfig"
    )
    Invoke-ReleaseDotnet $dotnet (@('build', $projectFile, '--no-incremental') + $buildOptions) 'Release 构建'
    Invoke-ReleaseDotnet $dotnet (@('build', $testProject) + $buildOptions) 'Release 测试项目构建'
    $testResults = Join-Path $runRoot 'test-results'
    Invoke-ReleaseDotnet $dotnet @(
        'test', '--project', $testProject, '-c', 'Release', '--no-build',
        '--minimum-expected-tests', '1', '--report-trx', '--report-trx-filename', 'tests.trx',
        '--results-directory', $testResults, '--no-ansi'
    ) '全部自动测试'
    $trx = [xml](Get-Content -LiteralPath (Join-Path $testResults 'tests.trx') -Raw -Encoding UTF8)
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 1 -or [int]$counters.executed -ne [int]$counters.total -or
        [int]$counters.passed -ne [int]$counters.total) {
        throw '必须实际执行全部测试并全部通过，不能以跳过或零测试结果发布。'
    }
    & (Join-Path $scriptDirectory 'test-release.ps1')
    Invoke-ReleaseDotnet $dotnet (
        @('publish', $projectFile, '-p:PublishProfile=Portable', "-p:PublishDir=$payload\") + $buildOptions
    ) '自包含发布'

    Assert-PortablePayload $payload
    $exe = Join-Path $payload 'TodayChecklist.exe'
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($fileVersion.FileVersion -ne "$version.0" -or
        ($fileVersion.ProductVersion -split '\+')[0] -ne $version) {
        throw 'EXE 文件版本与项目版本不一致。'
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PORTABLE.txt') -Destination (Join-Path $payload '使用说明.txt')

    # Only runtime packs listed in the produced deps file are redistributed.
    $deps = Get-Content -LiteralPath (Join-Path $payload 'TodayChecklist.deps.json') -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $licenseRoot = Join-Path $payload 'licenses'
    [void][IO.Directory]::CreateDirectory($licenseRoot)
    $licenseSources = [Collections.Generic.List[string]]::new()
    foreach ($library in $deps.libraries.PSObject.Properties) {
        if ($library.Name -eq "TodayChecklist/$version") { continue }
        if ($library.Name -notmatch '^runtimepack[.]([^/]+)/([^/]+)$') {
            throw "发现尚未配置许可证收集的运行依赖：$($library.Name)"
        }
        $packageId = $Matches[1]
        $packageVersion = $Matches[2]
        $package = Find-CachedReleasePackage @($assets.packageFolders.PSObject.Properties.Name) (
            $packageId.ToLowerInvariant() + '/' + $packageVersion)
        $licenseDirectory = Join-Path $licenseRoot "$packageId-$packageVersion"
        [void][IO.Directory]::CreateDirectory($licenseDirectory)
        $notices = @(Get-ChildItem -LiteralPath $package -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)([.]|$)' -or $_.Extension -eq '.nuspec' })
        if ($notices.Count -eq 0) { throw "依赖包缺少许可元数据：$packageId" }
        $hasLicense = @($notices | Where-Object Name -Match '^LICENSE([.]|$)').Count -gt 0
        if (-not $hasLicense) {
            $nuspec = @($notices | Where-Object Extension -EQ '.nuspec')
            if ($nuspec.Count -ne 1) { throw "无法确定依赖许可：$packageId" }
            $metadata = ([xml](Get-Content -LiteralPath $nuspec[0].FullName -Raw -Encoding UTF8)).package.metadata
            if ($null -eq $metadata.PSObject.Properties['licenseUrl'] -or
                [string]::IsNullOrWhiteSpace([string]$metadata.licenseUrl)) {
                throw "依赖包没有可附带的许可证或原始许可链接：$packageId"
            }
            $licenseSources.Add("$packageId $packageVersion : 原包仅提供许可链接，见原始 nuspec。")
        }
        else { $licenseSources.Add("$packageId $packageVersion : 已附带原包许可证及存在的第三方声明。") }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseDirectory $notice.Name)
        }
    }
    Write-ReleaseTextFile (Join-Path $licenseRoot '来源说明.txt') ($licenseSources -join [Environment]::NewLine)
    Assert-PortablePayload $payload
    $inventory = @(Get-ReleaseInventory $payload)
    if ($inventory.Count -eq 0) { throw '发布目录为空。' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipName = "TodayChecklist-$version-win-x64.zip"
    $zip = Join-Path $candidate $zipName
    $temporaryZip = Join-Path $runRoot 'package.zip.tmp'
    [IO.Compression.ZipFile]::CreateFromDirectory(
        (Split-Path -Parent $payload), $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    [IO.Compression.ZipFile]::ExtractToDirectory($temporaryZip, $verification)
    $topLevel = @(Get-ChildItem -LiteralPath $verification -Force)
    if ($topLevel.Count -ne 1 -or -not $topLevel[0].PSIsContainer -or $topLevel[0].Name -cne 'TodayChecklist') {
        throw 'ZIP 必须仅包含一个 TodayChecklist 文件夹。'
    }
    $extracted = Join-Path $verification 'TodayChecklist'
    Assert-ReleaseInventory $inventory $extracted
    Assert-PortablePayload $extracted
    $smoke = & (Join-Path $scriptDirectory 'smoke-test.ps1') -ExecutablePath (Join-Path $extracted 'TodayChecklist.exe')
    # Smoke must not create settings, logs, or tasks inside the distributable directory.
    Assert-ReleaseInventory $inventory $extracted
    Assert-ReleaseInventory $inventory $payload

    $hash = (Get-FileHash -LiteralPath $temporaryZip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::Move($temporaryZip, $zip)
    Write-ReleaseTextFile (Join-Path $candidate "$zipName.sha256") "$hash  $zipName"
    Write-ReleaseTextFile (Join-Path $candidate 'files.sha256.json') (
        ConvertTo-Json -InputObject $inventory -Depth 5)
    $record = [ordered]@{
        status = 'candidate-manual-acceptance-pending'
        version = $version
        target = 'Windows 11 x64'
        createdUtc = [DateTime]::UtcNow.ToString('o')
        buildWindowsVersion = [Environment]::OSVersion.VersionString
        sdkVersion = $sdkVersion
        zip = $zipName
        sha256 = $hash
        fileCount = $inventory.Count
        automatic = [ordered]@{
            releaseBuild = 'passed; warnings treated as errors'
            allTests = "passed: $($counters.passed) / $($counters.total)"
            releaseScriptTests = 'passed'
            selfContainedPayload = 'passed'
            exeVersion = 'passed'
            zipPathsLengthsAndSha256 = 'passed'
            extractedWpfSmokeTest = 'passed'
            chineseAndSpacePath = 'passed'
            differentWorkingDirectory = 'passed'
            payloadUnchangedAfterSmoke = 'passed'
            isolatedSession = $smoke.sessionDirectory
        }
        manual = [ordered]@{
            status = 'not-run'
            windowsVersion = $null
            cleanWindows11WithoutDotnet = 'pending'
            standardUserOfflineLaunch = 'pending'
            saveExitReopen = 'pending'
            existingFeatureChecklist = 'pending'
            upgradeWithSyntheticData = 'pending'
            optionalStartupAndDesktopMode = 'pending'
        }
    }
    Write-ReleaseTextFile (Join-Path $candidate 'acceptance.json') ($record | ConvertTo-Json -Depth 6)
    Write-ReleaseTextFile (Join-Path $candidate '验收记录.md') @"
# 今日清单 $version 发布候选包

目标系统：Windows 11 x64
构建主机：$([Environment]::OSVersion.VersionString)
SDK：$sdkVersion
ZIP：$zipName
SHA-256：$hash

自动检查已通过：Release 构建（警告视为错误）、全部测试、发布脚本回归测试、
自包含依赖检查、EXE 版本检查、ZIP 文件路径/大小/SHA-256 检查、
中文与空格路径解压后的 WPF 启动检查、不同工作目录、隔离会话、运行后发布文件不变。

## 手工验收尚未完成

- [ ] 记录实际验收 Windows 11 版本、日期和验收人。
- [ ] 干净系统，无开发工具和额外 .NET 桌面运行时；普通用户、断网解压启动。
- [ ] 新增、编辑、完成任务；从托盘退出再启动，数据保持正确。
- [ ] 单实例、托盘、图标、设置、导出导入和现有功能验收清单。
- [ ] 使用虚构数据验证旧版退出后，新目录的程序继续读取原有数据及设置。
- [ ] 验证可选开机启动及桌面模式；开机启动默认关闭。

当前状态：候选包，不能据此宣称已完成干净系统验收。
手工结果另存为 acceptance-manual.md，关联上述 ZIP 哈希，不覆盖此自动验收记录。
只有全部手工项目通过，才可将对应哈希的候选包标记为可发布。
"@
    # Validate the record and checksum before the one atomic directory commit.
    $validated = Get-Content -LiteralPath (Join-Path $candidate 'acceptance.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($validated.version -ne $version -or $validated.sha256 -cne $hash -or
        (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) {
        throw '最终发布记录或 ZIP 校验失败。'
    }
    Complete-ReleaseDirectory $projectRoot $candidate $destination
    Write-Host "候选包已生成：$(Join-Path $destination $zipName)"
    Write-Host '自动检查通过；干净 Windows 11 环境手工验收尚待完成。'
}
catch {
    Write-Host "发布未完成，现有发布包保持不变。本次现场：$runRoot"
    throw
}
finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}
