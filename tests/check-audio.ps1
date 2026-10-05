# SPDX-License-Identifier: MIT
$ErrorActionPreference='Stop'
$pvAudioFixtureDir=Join-Path $PSScriptRoot 'fixtures\audio'
New-Item -ItemType Directory -Path $pvAudioFixtureDir -Force | Out-Null
$pvAudioCases=@(
    @{Extension='wav';Codec='pcm_s16le'},
    @{Extension='mp3';Codec='libmp3lame'},
    @{Extension='flac';Codec='flac'},
    @{Extension='m4a';Codec='aac'},
    @{Extension='aac';Codec='aac'},
    @{Extension='ogg';Codec='libvorbis'},
    @{Extension='opus';Codec='libopus'},
    @{Extension='wma';Codec='wmav2'},
    @{Extension='aiff';Codec='pcm_s16be'},
    @{Extension='wv';Codec='wavpack'}
)
foreach($pvAudioCase in $pvAudioCases){
    $pvAudioFixture=Join-Path $pvAudioFixtureDir ('playback.'+$pvAudioCase.Extension)
    & ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=4' -c:a $pvAudioCase.Codec -metadata 'title=PV 播放检查' -metadata 'artist=PV 轻看' -metadata 'album=Audio Checks' $pvAudioFixture
    if($LASTEXITCODE -ne 0){throw ('Audio fixture generation failed: '+$pvAudioCase.Extension)}
}
$pvAudioCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pvAudioCheckExe=Join-Path $PSScriptRoot 'AudioPlaybackChecks.exe'
& $pvAudioCompiler /nologo /utf8output /target:exe /platform:x64 /r:System.Core.dll "/out:$pvAudioCheckExe" (Join-Path $PSScriptRoot '..\src\MpvPlayer.cs') (Join-Path $PSScriptRoot 'AudioPlaybackChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Audio check compilation failed'}
$pvAudioOriginalPath=$env:PATH
try{
    $env:PATH=(Join-Path $PSScriptRoot '..\dist')+';'+$pvAudioOriginalPath
    & $pvAudioCheckExe $pvAudioFixtureDir
    if($LASTEXITCODE -ne 0){throw 'Audio playback checks failed'}
}finally{$env:PATH=$pvAudioOriginalPath}
