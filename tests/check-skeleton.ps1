# SPDX-License-Identifier: MIT
param([string]$ModelFile)
$ErrorActionPreference='Stop'
$pvSkeletonRoot=Split-Path -Parent $PSScriptRoot
$pvSkeletonOut=Join-Path $pvSkeletonRoot 'work\skeleton-checks'
New-Item -ItemType Directory -Path $pvSkeletonOut -Force | Out-Null
$pvSkeletonCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pvSkeletonCheck=Join-Path $pvSkeletonOut 'SkeletonFbxChecks.exe'
$pvSkeletonLibrary=Join-Path $pvSkeletonRoot 'dist\pv-fbx.dll'
if(!(Test-Path -LiteralPath $pvSkeletonLibrary)){throw 'Build PV before running the skeleton checks.'}
Copy-Item -LiteralPath $pvSkeletonLibrary -Destination $pvSkeletonOut -Force
& $pvSkeletonCompiler /nologo /utf8output /target:exe /platform:x64 /r:System.Core.dll "/out:$pvSkeletonCheck" (Join-Path $pvSkeletonRoot 'src\ModelData.cs') (Join-Path $pvSkeletonRoot 'src\ModelPlayback.cs') (Join-Path $PSScriptRoot 'SkeletonFbxChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Skeleton check compilation failed'}
$pvSkeletonArguments=@((Join-Path $PSScriptRoot 'fixtures\skeleton'))
if($ModelFile){$pvSkeletonArguments += $ModelFile}
& $pvSkeletonCheck $pvSkeletonArguments
if($LASTEXITCODE -ne 0){throw 'Skeleton checks failed'}
