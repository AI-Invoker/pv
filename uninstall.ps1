# SPDX-License-Identifier: MIT
param([string]$Destination)
$ErrorActionPreference='Stop'
$pvRegistration='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PV'
if (!(Test-Path -LiteralPath $pvRegistration)) { throw '找不到 PV 的安装登记，已停止卸载。' }
$pvInstallLocation=(Get-ItemProperty -LiteralPath $pvRegistration).InstallLocation
if (!$pvInstallLocation) { throw '安装位置未登记，已停止卸载。' }
$pvExpected=[IO.Path]::GetFullPath($pvInstallLocation).TrimEnd('\')
if (!$Destination) { $Destination=$pvExpected }
$pvResolved=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($pvResolved -ne $pvExpected -or [IO.Path]::GetFileName($pvResolved) -ne 'PV') { throw '卸载仅限已登记的 PV 安装目录。' }
if ((Test-Path -LiteralPath $pvResolved) -and ((Get-Item -LiteralPath $pvResolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目录为链接，已停止自动删除。' }
$pvRunning=Get-Process -Name PV -ErrorAction SilentlyContinue | Where-Object Path -EQ (Join-Path $pvResolved 'PV.exe')
if ($pvRunning) { throw '请先关闭 PV 轻看后再卸载。' }
$pvImage=@('.jpg','.jpeg','.jpe','.jfif','.png','.bmp','.gif','.tif','.tiff','.ico','.webp','.avif','.heic','.heif')
$pvVideo=@('.mp4','.mkv','.mov','.avi','.webm','.wmv','.m4v','.mpg','.mpeg','.ts','.mts','.m2ts','.flv','.3gp','.ogv','.vob')
$pvAudio=@('.mp3','.wav','.flac','.m4a','.m4b','.aac','.ogg','.oga','.opus','.wma','.aif','.aiff','.ape','.wv','.mka')
foreach ($pvExt in ($pvImage+$pvVideo+$pvAudio+@('.fbx'))) {
    $pvProg='PV.'+$pvExt.Substring(1)
    $pvClass='HKCU:\Software\Classes\'+$pvProg
    if (Test-Path -LiteralPath $pvClass) { Remove-Item -LiteralPath $pvClass -Recurse }
    $pvOpen='HKCU:\Software\Classes\'+$pvExt+'\OpenWithProgids'
    if (Test-Path -LiteralPath $pvOpen) { Remove-ItemProperty -LiteralPath $pvOpen -Name $pvProg -ErrorAction SilentlyContinue }
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\RegisteredApplications' -Name 'PV 轻看' -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath 'HKCU:\Software\RegisteredApplications' -Name 'PV' -ErrorAction SilentlyContinue
foreach ($pvKey in @('HKCU:\Software\PV','HKCU:\Software\Clients\Media\PV','HKCU:\Software\Classes\Applications\PV.exe','HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\PV.exe','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PV')) { if(Test-Path -LiteralPath $pvKey){Remove-Item -LiteralPath $pvKey -Recurse} }
foreach ($pvLink in @((Join-Path ([Environment]::GetFolderPath('Programs')) 'PV 轻看.lnk'),(Join-Path ([Environment]::GetFolderPath('Desktop')) 'PV 轻看.lnk'))) { if(Test-Path -LiteralPath $pvLink){Remove-Item -LiteralPath $pvLink} }
if (Test-Path -LiteralPath $pvResolved) { Remove-Item -LiteralPath $pvResolved -Recurse }
Write-Output 'PV 已卸载。个人偏好保留在 %LOCALAPPDATA%\PV；下次打开文件时可选择新的默认应用。'
