# Shared release checks. No downloads, registry changes, or destructive cleanup.
Set-StrictMode -Version Latest

function Assert-ReleaseChildPath {
    param([string]$Root, [string]$Path)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "路径超出允许的工作目录：$fullPath"
    }
    $cursor = $fullPath
    while ($cursor.Length -ge $rootPath.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "发布路径不能经过链接或重解析点：$cursor"
            }
        }
        if ($cursor -eq $rootPath) { break }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $fullPath
}

function New-ReleaseWorkspace {
    param([string]$ProjectRoot, [string]$Category)
    if ($Category -notmatch '^[a-z-]+$') { throw '工作目录类别无效。' }
    $path = Assert-ReleaseChildPath $ProjectRoot (
        Join-Path $ProjectRoot ("artifacts\{0}\run-{1}" -f $Category, [Guid]::NewGuid().ToString('N')))
    [void][IO.Directory]::CreateDirectory($path)
    return $path
}

function Write-ReleaseTextFile {
    param([string]$Path, [string]$Text)
    if (Test-Path -LiteralPath $Path) { throw "不会覆盖已有文件：$Path" }
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporary, $Text, [Text.UTF8Encoding]::new($true))
    if ([IO.File]::ReadAllText($temporary) -cne $Text) { throw '文本写入验证失败。' }
    [IO.File]::Move($temporary, $Path)
}

function Get-ReleaseFiles {
    param([string]$Directory)
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push([IO.Path]::GetFullPath($Directory))
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        $rootItem = Get-Item -LiteralPath $current -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "不允许打包链接目录：$current"
        }
        foreach ($item in Get-ChildItem -LiteralPath $current -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "不允许打包链接：$($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            else { $item }
        }
    }
}

function Get-ReleaseInventory {
    param([string]$Directory)
    $prefix = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    foreach ($file in (Get-ReleaseFiles $Directory | Sort-Object FullName)) {
        [pscustomobject][ordered]@{
            path = $file.FullName.Substring($prefix.Length).Replace('\', '/')
            length = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

function Assert-ReleaseInventory {
    param([object[]]$Expected, [string]$Directory)
    $actual = @(Get-ReleaseInventory $Directory)
    if ($actual.Count -ne $Expected.Count) { throw '发布文件数量校验失败。' }
    $lookup = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in $Expected) { $lookup.Add($file.path, $file) }
    foreach ($file in $actual) {
        if (-not $lookup.ContainsKey($file.path)) { throw "发布文件路径校验失败：$($file.path)" }
        $before = $lookup[$file.path]
        if ($file.length -ne $before.length -or $file.sha256 -cne $before.sha256) {
            throw "发布文件内容校验失败：$($file.path)"
        }
    }
}

function Assert-PortablePayload {
    param([string]$Directory)
    foreach ($required in @('TodayChecklist.exe', 'TodayChecklist.dll', 'TodayChecklist.deps.json',
        'TodayChecklist.runtimeconfig.json', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll',
        'PresentationFramework.dll', 'System.Private.CoreLib.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $required) -PathType Leaf)) {
            throw "发布目录缺少必要文件：$required"
        }
    }
    $prefix = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    foreach ($file in Get-ReleaseFiles $Directory) {
        $relative = $file.FullName.Substring($prefix.Length)
        if ($relative -match '(^|\\)(data|backups|logs|TestResults|bin|obj|\.git|\.vs)(\\|$)' -or
            $file.Name -match '(^tasks[.]|^settings[.]|^location[.]|^\.todaychecklist-|TodayChecklist[.]Tests|testhost|MSTest|Microsoft[.]Testing|ApplicationInsights)' -or
            $file.Extension -match '^\.(cs|csproj|ps1|pdb|log|tmp)$' -or
            ($file.Extension -eq '.json' -and $file.Name -notin @('TodayChecklist.deps.json', 'TodayChecklist.runtimeconfig.json'))) {
            throw "发布目录含有不应分发的文件：$relative"
        }
    }
    $options = (Get-Content -LiteralPath (Join-Path $Directory 'TodayChecklist.runtimeconfig.json') -Raw -Encoding UTF8 |
        ConvertFrom-Json).runtimeOptions
    if ($null -ne $options.PSObject.Properties['framework'] -or
        $null -ne $options.PSObject.Properties['frameworks'] -or
        $null -eq $options.PSObject.Properties['includedFrameworks']) {
        throw '发布结果不是自包含应用。'
    }
    foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        if ($framework -notin @($options.includedFrameworks | ForEach-Object { $_.name })) {
            throw "发布结果缺少内置运行时：$framework"
        }
    }
}

function Find-CachedReleasePackage {
    param([string[]]$PackageFolders, [string]$RelativePath)
    foreach ($folder in $PackageFolders) {
        $candidate = Join-Path $folder $RelativePath
        if (Test-Path -LiteralPath (Join-Path $candidate '.nupkg.metadata') -PathType Leaf) {
            return $candidate
        }
    }
    throw "缺少已缓存的依赖包：$RelativePath。已停止；不会自动下载或安装。"
}

function Assert-CachedReleaseAssets {
    param([string]$ProjectFile)
    $assetsFile = Join-Path (Split-Path -Parent $ProjectFile) 'obj\project.assets.json'
    if (-not (Test-Path -LiteralPath $assetsFile -PathType Leaf)) {
        throw "缺少已有依赖清单：$assetsFile。需要先单独准备依赖；本脚本不执行还原。"
    }
    $assets = Get-Content -LiteralPath $assetsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $folders = @($assets.packageFolders.PSObject.Properties.Name)
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $package = Find-CachedReleasePackage $folders $library.Value.path
        foreach ($file in $library.Value.files) {
            if (-not (Test-Path -LiteralPath (Join-Path $package $file) -PathType Leaf)) {
                throw "依赖缓存不完整：$($library.Name)/$file。不会自动下载。"
            }
        }
    }
    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        if ($null -eq $framework.Value.PSObject.Properties['downloadDependencies']) { continue }
        foreach ($dependency in $framework.Value.downloadDependencies) {
            if ($dependency.version -notmatch '^\[([^,]+),\s*\1\]$') {
                throw "无法确定缓存依赖的精确版本：$($dependency.name)"
            }
            [void](Find-CachedReleasePackage $folders ($dependency.name.ToLowerInvariant() + '/' + $Matches[1]))
        }
    }
    return $assets
}

function Invoke-ReleaseDotnet {
    param([string]$DotnetPath, [string[]]$Arguments, [string]$Label)
    Write-Host $Label
    & $DotnetPath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Label 失败，退出代码：$LASTEXITCODE。已停止发布。" }
}

function Wait-ReleaseProcess {
    param([Diagnostics.Process]$Process, [string]$ExecutablePath, [int]$TimeoutMilliseconds = 30000)
    if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
        # Keep the original process object/handle; never look up or kill by process name.
        if (-not $Process.HasExited) {
            $actual = [IO.Path]::GetFullPath($Process.MainModule.FileName)
            if (-not $actual.Equals([IO.Path]::GetFullPath($ExecutablePath), [StringComparison]::OrdinalIgnoreCase)) {
                throw '测试进程目标不匹配，拒绝结束该进程。'
            }
            $Process.Kill()
            [void]$Process.WaitForExit(5000)
        }
        throw "隔离烟雾测试超时（$TimeoutMilliseconds ms）；仅结束本次创建且验证过目标的测试进程。"
    }
    if ($Process.ExitCode -ne 0) { throw "隔离烟雾测试失败，退出代码：$($Process.ExitCode)。" }
}

function Complete-ReleaseDirectory {
    param([string]$ProjectRoot, [string]$Candidate, [string]$Destination)
    $source = Assert-ReleaseChildPath $ProjectRoot $Candidate
    $target = Assert-ReleaseChildPath $ProjectRoot $Destination
    if (Test-Path -LiteralPath $target) { throw "该版本已存在，不会覆盖：$target" }
    if ([IO.Path]::GetPathRoot($source) -ne [IO.Path]::GetPathRoot($target)) {
        throw '候选目录与最终目录必须在同一卷。'
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    # Move is atomic on the same volume and fails if another run already committed this version.
    [IO.Directory]::Move($source, $target)
}
