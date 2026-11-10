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
Log "开始构建 STranslate 版本: $CleanVersion" "Green"

# 构建解决方案
Log "正在重新生成解决方案..."
dotnet build "$PSScriptRoot\src\STranslate.slnx" `
  --configuration Release `
  --no-incremental `
  /p:Version=$CleanVersion `
  /p:PackageVersion=$CleanVersion `
  /p:InformationalVersion=$CleanVersion


if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码：$LASTEXITCODE" }

Log "构建完成！" "Green"
