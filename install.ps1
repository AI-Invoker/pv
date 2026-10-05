# SPDX-License-Identifier: MIT
param([string]$Destination, [switch]$OpenDefaults)
$ErrorActionPreference = 'Stop'
$pvDist = Join-Path $PSScriptRoot 'dist'
$pvUninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PV'
if (!$Destination) {
    if (Test-Path -LiteralPath $pvUninstall) { $Destination=(Get-ItemProperty -LiteralPath $pvUninstall).InstallLocation }
    if (!$Destination) { $Destination=Join-Path $env:LOCALAPPDATA 'Programs\PV' }
}
$Destination=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ([IO.Path]::GetFileName($Destination) -ne 'PV') { throw '请将安装目录命名为 PV，例如 C:\Apps\PV。' }
$pvExe = Join-Path $Destination 'PV.exe'
$pvRunning = Get-Process -Name PV -ErrorAction SilentlyContinue | Where-Object Path -EQ $pvExe
if ($pvRunning) { throw '请先关闭 PV 轻看窗口，再执行安装更新。' }
foreach ($pvRequired in @('PV.exe','PV.exe.config','libmpv-2.dll','pv-fbx.dll','LICENSE-ufbx.txt','LICENSE-stb.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $pvDist $pvRequired))) { throw "缺少构建文件：$pvRequired" }
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Get-ChildItem -LiteralPath $pvDist | Where-Object { $_.PSIsContainer -or $_.Extension -NotIn @('.lib','.pdb','.a') } | Copy-Item -Destination $Destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $Destination -Force
$pvRegister = Start-Process -FilePath $pvExe -ArgumentList '--register' -WindowStyle Hidden -Wait -PassThru
if ($pvRegister.ExitCode -ne 0) { throw 'PV registration failed' }
$pvShell = New-Object -ComObject WScript.Shell
$pvMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PV 轻看.lnk'
$pvDesktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PV 轻看.lnk'
foreach ($pvLink in @($pvMenu,$pvDesktop)) { $pvShortcut=$pvShell.CreateShortcut($pvLink); $pvShortcut.TargetPath=$pvExe; $pvShortcut.WorkingDirectory=$Destination; $pvShortcut.Description='图片、视频、音乐与 FBX 查看器 · 支持倍速播放'; $pvShortcut.IconLocation="$pvExe,0"; $pvShortcut.Save() }
New-Item -Path $pvUninstall -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name DisplayName -Value 'PV 轻看' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name DisplayVersion -Value '1.4.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name DisplayIcon -Value "$pvExe,0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name InstallLocation -Value $Destination -PropertyType String -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Destination\uninstall.ps1`" -Destination `"$Destination`"" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $pvUninstall -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
if ($OpenDefaults) { Start-Process 'ms-settings:defaultapps?registeredAppUser=PV' }
Write-Output "Installed: $pvExe"
