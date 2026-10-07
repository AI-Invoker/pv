# SPDX-License-Identifier: MIT
param([string]$Executable)
$ErrorActionPreference='Stop'
$pvActivationRoot=Join-Path $PSScriptRoot '..\work\foreground-activation'
New-Item -ItemType Directory -Path $pvActivationRoot -Force | Out-Null
$pvActivationCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pvActivationCheck=Join-Path $pvActivationRoot 'WindowActivationChecks.exe'
& $pvActivationCompiler /nologo /utf8output /target:exe /platform:x64 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$pvActivationCheck" (Join-Path $PSScriptRoot 'WindowActivationChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Activation check compilation failed'}
if(!$Executable){
    # Give the test viewer its own application identity, separate from a live PV window.
    $Executable=Join-Path $pvActivationRoot 'PVActivationFixture.exe'
    $pvActivationSources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\src') -Filter '*.cs' | ForEach-Object FullName)
    $pvActivationIcons=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\assets\ui') -Filter '*.svg' | ForEach-Object {"/resource:$($_.FullName),PV.Icons.$($_.Name)"})
    $pvActivationVB=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll'
    & $pvActivationCompiler /nologo /utf8output /target:winexe /platform:x64 /optimize+ "/out:$Executable" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll "/r:$pvActivationVB" $pvActivationIcons $pvActivationSources
    if($LASTEXITCODE -ne 0){throw 'Activation fixture compilation failed'}
}
$pvActivationOriginalPath=$env:PATH
try{
    $env:PATH=(Join-Path $PSScriptRoot '..\dist')+';'+$pvActivationOriginalPath
    & $pvActivationCheck $Executable (Join-Path $pvActivationRoot 'media')
    if($LASTEXITCODE -ne 0){throw 'Window activation checks failed'}
}finally{$env:PATH=$pvActivationOriginalPath}
