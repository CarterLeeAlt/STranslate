# 从命令行参数获取版本号,如果未提供则使用默认值
param(
    [string]$Version = "2.0.0",
    [string]$OutputDirectory = "$PSScriptRoot\publish\portable"
)

$ErrorActionPreference = "Stop"

function Log([string]$msg, [string]$color = "Yellow") {
    Write-Host $msg -ForegroundColor $color
}

# 去除版本号中的 'v' 前缀(如果存在)
$CleanVersion = $Version -replace '^v', ''

Log "开始打包 STranslate 版本: $CleanVersion" "Green"

# 检查源目录是否存在
$SourcePath = "$PSScriptRoot\src\.artifacts\Release"
if (-not (Test-Path $SourcePath)) {
    Log "错误: 源目录不存在: $SourcePath" "Red"
    exit 1
}

$ZipName = "STranslate-win-Portable.zip"
$Destination = Join-Path $OutputDirectory $ZipName
if (Test-Path -LiteralPath $Destination) {
    throw "交付包已存在：$Destination；请指定新的输出目录。"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# 压缩 Release 产物（根目录布局，与本地安装目录一致）
Log "正在压缩 Release 产物..." "Cyan"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# 根目录布局；插件使用宿主的 STranslate.Plugin 程序集。
$zip = [System.IO.Compression.ZipFile]::Open($Destination, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $root = (Resolve-Path $SourcePath).Path
    Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
        if ($relative -match '^PortableConfig/' -or $relative -match '^Plugins/.*/STranslate\.Plugin\.(dll|xml|pdb)$') { return }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $zip.CreateEntry("PortableConfig/") | Out-Null
} finally {
    $zip.Dispose()
}

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
Log "  - $Destination（$entryCount 项，含 PortableConfig 目录）" "White"
