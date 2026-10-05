# SPDX-License-Identifier: MIT
param([switch]$Install, [string]$Compiler)
$ErrorActionPreference = 'Stop'
$pvRoot = $PSScriptRoot
$pvCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pvVB = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll'
$pvDist = Join-Path $pvRoot 'dist'
foreach ($pvDependency in @('vendor\mpv\libmpv-2.dll','vendor\ufbx\ufbx.c','vendor\ufbx\ufbx.h','vendor\stb\stb_image.h')) {
    if (!(Test-Path -LiteralPath (Join-Path $pvRoot $pvDependency))) { throw "依赖缺失：$pvDependency。请先运行 prepare-dependencies.ps1。" }
}
New-Item -ItemType Directory -Path $pvDist -Force | Out-Null
$pvNative = Join-Path $pvDist 'pv-fbx.dll'
$pvNativeInputs = @('native\pv_fbx.c','vendor\ufbx\ufbx.c','vendor\ufbx\ufbx.h','vendor\stb\stb_image.h','build-native.ps1') | ForEach-Object { Get-Item -LiteralPath (Join-Path $pvRoot $_) }
if (!(Test-Path -LiteralPath $pvNative) -or @($pvNativeInputs | Where-Object LastWriteTimeUtc -GT (Get-Item -LiteralPath $pvNative -ErrorAction SilentlyContinue).LastWriteTimeUtc).Count -gt 0) { & "$pvRoot\build-native.ps1" -Compiler $Compiler }
$pvSource = @(Get-ChildItem -LiteralPath (Join-Path $pvRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$pvIcons = @(Get-ChildItem -LiteralPath (Join-Path $pvRoot 'assets\ui') -Filter '*.svg' | ForEach-Object { "/resource:$($_.FullName),PV.Icons.$($_.Name)" })
& $pvCompiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$pvDist\PV.exe" "/win32icon:$pvRoot\assets\PV.ico" "/win32manifest:$pvRoot\assets\PV.manifest" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll "/r:$pvVB" $pvIcons $pvSource
if ($LASTEXITCODE -ne 0) { throw 'PV compilation failed' }
Copy-Item -LiteralPath "$pvRoot\assets\PV.exe.config" -Destination $pvDist -Force
Copy-Item -LiteralPath "$pvRoot\vendor\mpv\libmpv-2.dll" -Destination $pvDist -Force
Copy-Item -LiteralPath "$pvRoot\README.txt","$pvRoot\THIRD-PARTY-NOTICES.txt","$pvRoot\LICENSE" -Destination $pvDist -Force
New-Item -ItemType Directory -Path (Join-Path $pvDist 'LICENSES') -Force | Out-Null
Copy-Item -Path "$pvRoot\LICENSES\*.txt" -Destination (Join-Path $pvDist 'LICENSES') -Force
if ($Install) { & "$pvRoot\install.ps1" }
Get-ChildItem -LiteralPath $pvDist | Select-Object Name,Length
