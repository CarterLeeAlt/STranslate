# 热替换部署构建输出到本地便携安装目录，并内置部署后完整性验证。
# 用法：pwsh -NoProfile -File deploy-local.ps1 [-Src 构建输出目录] [-Dst 安装目录]
# 验证失败的教训：曾因 git stash 对照测试覆盖构建产物后未重建就部署，
# 造成"已成功生成"的假部署——因此本脚本末尾强制比对关键文件哈希。

param(
    [string]$Src = "$PSScriptRoot\src\.artifacts\Debug",
    [string]$Dst = 'C:\UserProgram\STranslate-win-Portable',
    # 参与部署后哈希验证的关键文件（相对路径）
    [string[]]$VerifyFiles = @('STranslate.exe', 'STranslate.dll', 'STranslate.Plugin.dll')
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$copied = 0; $renamed = 0; $failed = [System.Collections.Generic.List[string]]::new()

# 用户数据目录绝不触碰
$files = Get-ChildItem -LiteralPath $Src -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\PortableConfig\\'
}

foreach ($f in $files) {
    $rel = $f.FullName.Substring($Src.Length).TrimStart('\')
    $target = Join-Path $Dst $rel
    $targetDir = Split-Path $target -Parent
    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    }
    if (Test-Path -LiteralPath $target) {
        $same = ((Get-Item -LiteralPath $target).Length -eq $f.Length) -and
            ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)
        if ($same) { $copied++; continue }
    }
    try {
        Copy-Item -LiteralPath $f.FullName -Destination $target -Force
        $copied++
    }
    catch [System.IO.IOException], [System.UnauthorizedAccessException] {
        # 被运行中进程锁定的文件：重命名旧文件后放新文件（Windows 允许重命名运行中 exe）
        try {
            Rename-Item -LiteralPath $target -NewName ("{0}.old-{1}" -f (Split-Path $target -Leaf), $stamp)
            Copy-Item -LiteralPath $f.FullName -Destination $target -Force
            $renamed++
        }
        catch {
            $failed.Add("$rel : $($_.Exception.Message)")
        }
    }
}

Write-Host "copied/skipped-same: $copied, locked-renamed: $renamed, failed: $($failed.Count)"
$failed | ForEach-Object { Write-Host "FAILED $_" }

# 部署后验证：关键文件必须与构建产物完全一致，防止假部署
$mismatch = [System.Collections.Generic.List[string]]::new()
foreach ($rel in $VerifyFiles) {
    $srcFile = Join-Path $Src $rel
    $dstFile = Join-Path $Dst $rel
    if (-not (Test-Path -LiteralPath $srcFile) -or -not (Test-Path -LiteralPath $dstFile)) {
        $mismatch.Add("$rel 缺失")
        continue
    }
    if ((Get-FileHash -LiteralPath $srcFile -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $dstFile -Algorithm SHA256).Hash) {
        $mismatch.Add("$rel 与构建产物不一致")
    }
}

if ($failed.Count -gt 0 -or $mismatch.Count -gt 0) {
    $mismatch | ForEach-Object { Write-Host "VERIFY-FAILED $_" }
    Write-Host '部署验证未通过！请重新构建后再试。'
    exit 1
}

Write-Host "部署验证通过：$($VerifyFiles -join ', ') 与构建产物完全一致。运行中的旧进程需重启后生效。"
