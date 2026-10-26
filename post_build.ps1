# 从命令行参数获取版本号,如果未提供则使用默认值
param(
    [string]$Version = "2.0.0"
)

$ErrorActionPreference = "Stop"

function Log([string]$msg, [string]$color = "Yellow") {
    Write-Host $msg -ForegroundColor $color
}

# 去除版本号中的 'v' 前缀(如果存在)
$CleanVersion = $Version -replace '^v', ''

Log "开始打包 STranslate 版本: $CleanVersion" "Green"

# 检查源目录是否存在
$SourcePath = ".\src\.artifacts\Release\"
if (-not (Test-Path $SourcePath)) {
    Log "错误: 源目录不存在: $SourcePath" "Red"
    exit 1
}

# 中间文件放在构建产物目录，publish 只保留最终交付文件
$PackPath = ".\src\.artifacts\pack\$CleanVersion"
New-Item -ItemType Directory -Path $PackPath -Force | Out-Null

$ZipName = "STranslate-win-Portable.zip"
$ZipPath = Join-Path $PackPath $ZipName

# 压缩 Release 产物（根目录布局，与本地安装目录一致）
Log "正在压缩 Release 产物..." "Cyan"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    (Resolve-Path $SourcePath).Path,
    $ZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

# 写入空的 PortableConfig 目录，解压后即为便携模式
$zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $existing = $zip.GetEntry("PortableConfig/")
    if ($existing) {
        $existing.Delete()
    }
    $zip.CreateEntry("PortableConfig/") | Out-Null
} finally {
    $zip.Dispose()
}

# 整理到 publish/portable，并只保留最新版
Log "整理交付文件..." "Cyan"

$OutputPath = ".\publish"
$PortableDir = Join-Path $OutputPath "portable"
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null

# 清理旧版更新体系留下的目录
foreach ($legacy in @("setup", "update", "metadata")) {
    $legacyPath = Join-Path $OutputPath $legacy
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Recurse -Force
    }
}

# 只保留最新版便携包
Get-ChildItem -Path $PortableDir -Filter "*.zip" -File | Remove-Item -Force

$Destination = Join-Path $PortableDir $ZipName
Move-Item -LiteralPath $ZipPath -Destination $Destination -Force

# 校验压缩包
$zip = [System.IO.Compression.ZipFile]::OpenRead($Destination)
$entryCount = $zip.Entries.Count
$hasPortableConfig = $null -ne $zip.GetEntry("PortableConfig/")
$zip.Dispose()

if (-not $hasPortableConfig) {
    Log "错误: 压缩包中缺少 PortableConfig 目录" "Red"
    exit 1
}

Log "打包完成!" "Green"
Log "生成的文件:" "Cyan"
Log "  - portable\$ZipName（$entryCount 项，含 PortableConfig 目录）" "White"
