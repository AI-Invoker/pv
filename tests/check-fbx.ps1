# SPDX-License-Identifier: MIT
param([switch]$Gui)
$ErrorActionPreference='Stop'
$pvFixtureDirectory=Join-Path $PSScriptRoot 'fixtures\fbx'
New-Item -ItemType Directory -Path $pvFixtureDirectory -Force | Out-Null
# Upstream ufbx regression assets stay in ignored tests/fixtures.
$pvFixtureRevision='26a482ae66871d7de36eb722aa060bce95bce274'
$pvFixtureNames=@('blender_279_sausage_7400_binary.fbx','blender_279_sausage_6100_ascii.fbx','blender440_shape_weight_anim_7400_binary.fbx','blender_272_cube_7400_binary.fbx','maya_anim_layers_7500_ascii.fbx','maya_game_sausage_7500_binary_combined.fbx','maya_game_sausage_6100_ascii_combined.fbx','maya_blend_shape_cube_7700_binary.fbx','max2009_cube_anim_6100_ascii.fbx','maya_anim_pivot_rotate_7700_ascii.fbx')
foreach($pvFixtureName in $pvFixtureNames){
    $pvFixturePath=Join-Path $pvFixtureDirectory $pvFixtureName
    if(!(Test-Path -LiteralPath $pvFixturePath)){Invoke-WebRequest -UseBasicParsing -Uri "https://raw.githubusercontent.com/ufbx/ufbx/$pvFixtureRevision/data/$pvFixtureName" -OutFile $pvFixturePath}
}
$pvCheckCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pvCheckExe=Join-Path $PSScriptRoot 'FbxAnimationChecks.exe'
& $pvCheckCompiler /nologo /utf8output /target:exe /platform:x64 /r:System.Core.dll "/out:$pvCheckExe" (Join-Path $PSScriptRoot '..\src\ModelData.cs') (Join-Path $PSScriptRoot '..\src\ModelPlayback.cs') (Join-Path $PSScriptRoot 'FbxAnimationChecks.cs')
if($LASTEXITCODE -ne 0){throw 'FBX check compilation failed'}
$pvCheckOriginalPath=$env:PATH
try{
    $env:PATH=(Join-Path $PSScriptRoot '..\dist')+';'+$pvCheckOriginalPath
    & $pvCheckExe $pvFixtureDirectory
    if($LASTEXITCODE -ne 0){throw 'FBX animation checks failed'}
    if($Gui){
        $pvGuiCheckExe=Join-Path $PSScriptRoot 'ViewerLifecycleChecks.exe'
        $pvGuiSources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\src') -Filter '*.cs' | ForEach-Object FullName)
        $pvGuiIcons=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\assets\ui') -Filter '*.svg' | ForEach-Object {"/resource:$($_.FullName),PV.Icons.$($_.Name)"})
        $pvVisualBasic=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll'
        & $pvCheckCompiler /nologo /utf8output /target:exe /platform:x64 /main:PV.ViewerLifecycleChecks "/out:$pvGuiCheckExe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll "/r:$pvVisualBasic" $pvGuiIcons $pvGuiSources (Join-Path $PSScriptRoot 'ViewerLifecycleChecks.cs')
        if($LASTEXITCODE -ne 0){throw 'Viewer lifecycle check compilation failed'}
        & $pvGuiCheckExe $pvFixtureDirectory
        if($LASTEXITCODE -ne 0){throw 'Viewer lifecycle checks failed'}
    }
}finally{$env:PATH=$pvCheckOriginalPath}
