# SPDX-License-Identifier: MIT
param([switch]$SkipCompiler)
$ErrorActionPreference = 'Stop'
$pvRoot = $PSScriptRoot
$pvDependencies = Get-Content -LiteralPath (Join-Path $pvRoot 'dependencies.json') -Raw | ConvertFrom-Json
$pvDownloads = Join-Path $pvRoot 'work\downloads'
New-Item -ItemType Directory -Path $pvDownloads -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Test-PvHash([string]$Path, [string]$Expected) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Expected)
}
function Get-PvDownload([string]$Url, [string]$Filename, [string]$Sha256) {
    $pvDownloadPath = Join-Path $pvDownloads $Filename
    if (Test-PvHash $pvDownloadPath $Sha256) { return $pvDownloadPath }
    $pvPartialPath = $pvDownloadPath + '.download'
    Write-Host "下载：$Filename"
    Invoke-WebRequest -Uri $Url -OutFile $pvPartialPath -UseBasicParsing
    if (!(Test-PvHash $pvPartialPath $Sha256)) { throw "下载校验失败：$Filename。文件保留在 $pvPartialPath，未执行或解压。" }
    Move-Item -LiteralPath $pvPartialPath -Destination $pvDownloadPath -Force
    return $pvDownloadPath
}

foreach ($pvSourceCheck in @(
    @{Path='vendor\ufbx\ufbx.c'; Hash=$pvDependencies.ufbx.c_sha256},
    @{Path='vendor\ufbx\ufbx.h'; Hash=$pvDependencies.ufbx.h_sha256},
    @{Path='vendor\stb\stb_image.h'; Hash=$pvDependencies.stb_image.sha256}
)) {
    if (!(Test-PvHash (Join-Path $pvRoot $pvSourceCheck.Path) $pvSourceCheck.Hash)) { throw "第三方源码缺失或版本不符：$($pvSourceCheck.Path)" }
}

$pvMpvDirectory = Join-Path $pvRoot 'vendor\mpv'
$pvMpvLibrary = Join-Path $pvMpvDirectory 'libmpv-2.dll'
if (!(Test-PvHash $pvMpvLibrary $pvDependencies.mpv.dll_sha256)) {
    $pvArchive = Get-PvDownload $pvDependencies.mpv.url $pvDependencies.mpv.archive $pvDependencies.mpv.sha256
    New-Item -ItemType Directory -Path $pvMpvDirectory -Force | Out-Null
    $pvTar = Join-Path $env:WINDIR 'System32\tar.exe'
    $pvExtracted = $false
    if (Test-Path -LiteralPath $pvTar) {
        & $pvTar -xf $pvArchive -C $pvMpvDirectory
        $pvExtracted = ($LASTEXITCODE -eq 0)
    }
    if (!$pvExtracted) {
        $pvSevenZip = Get-Command '7z.exe' -ErrorAction SilentlyContinue
        $pvSevenZipPath = if ($pvSevenZip) { $pvSevenZip.Source } else { Join-Path $env:ProgramFiles '7-Zip\7z.exe' }
        if (!(Test-Path -LiteralPath $pvSevenZipPath)) { throw '解压 mpv 需要支持 7z 的 Windows tar.exe 或 7-Zip。安装 7-Zip 后重新运行此脚本。' }
        & $pvSevenZipPath x $pvArchive "-o$pvMpvDirectory" -y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'mpv 解压失败。' }
    }
    if (!(Test-PvHash $pvMpvLibrary $pvDependencies.mpv.dll_sha256)) { throw 'libmpv-2.dll 校验失败。' }
}

if (!$SkipCompiler) {
    $pvToolchain = Join-Path $pvRoot 'work\toolchain'
    $pvZig = Join-Path (Join-Path $pvToolchain $pvDependencies.zig.directory) 'zig.exe'
    if (!(Test-Path -LiteralPath $pvZig -PathType Leaf)) {
        $pvArchive = Get-PvDownload $pvDependencies.zig.url $pvDependencies.zig.archive $pvDependencies.zig.sha256
        New-Item -ItemType Directory -Path $pvToolchain -Force | Out-Null
        Write-Host "解压 Zig $($pvDependencies.zig.version)…"
        Expand-Archive -LiteralPath $pvArchive -DestinationPath $pvToolchain -Force
    }
    $pvZigVersion = & $pvZig version
    if ($LASTEXITCODE -ne 0 -or $pvZigVersion -ne $pvDependencies.zig.version) { throw 'Zig 编译器版本不符。' }
}
Write-Host '依赖准备完成，可运行 .\build.ps1。'
