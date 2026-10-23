# SPDX-License-Identifier: MIT
$ErrorActionPreference='Stop'
$pvRoot=Split-Path -Parent $PSScriptRoot
$pvSvgOutput=Join-Path $pvRoot 'work\svg-checks'
New-Item -ItemType Directory -Path $pvSvgOutput -Force | Out-Null
$pvSvgCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pvSvgVB=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll'
$pvSvgLibrary=Join-Path $pvRoot 'vendor\svg\Svg.dll'
if(!(Test-Path -LiteralPath $pvSvgLibrary)){throw 'Run prepare-dependencies.ps1 before the SVG checks.'}
$pvSvgViewer=Join-Path $pvRoot 'dist\PV.exe'
if(!(Test-Path -LiteralPath $pvSvgViewer)){throw 'Run build.ps1 before the SVG checks.'}
Copy-Item -Path "$pvRoot\vendor\svg\*.dll" -Destination $pvSvgOutput -Force
$pvSvgExe=Join-Path $pvSvgOutput 'SvgImageChecks.exe'
$pvSvgSources=@(Get-ChildItem -LiteralPath (Join-Path $pvRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $pvSvgCompiler /nologo /utf8output /target:exe /platform:x64 /main:PV.SvgImageChecks "/out:$pvSvgExe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll "/r:$pvSvgVB" "/r:$pvSvgLibrary" $pvSvgSources (Join-Path $PSScriptRoot 'SvgImageChecks.cs')
if($LASTEXITCODE -ne 0){throw 'SVG check compilation failed'}
& $pvSvgExe (Join-Path $PSScriptRoot 'fixtures\svg') $pvSvgViewer
if($LASTEXITCODE -ne 0){throw 'SVG checks failed'}
