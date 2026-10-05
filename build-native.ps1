# SPDX-License-Identifier: MIT
param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$pvRoot = $PSScriptRoot
if (!$Compiler) { $Compiler = Join-Path $pvRoot 'work\toolchain\zig-x86_64-windows-0.14.1\zig.exe' }
if (!(Test-Path -LiteralPath $Compiler)) { throw '需要 Zig 0.14.1 Windows x64 编译器；用 -Compiler 指定 zig.exe。下载地址见 README.txt。' }
$pvDist = Join-Path $pvRoot 'dist'
New-Item -ItemType Directory -Path $pvDist -Force | Out-Null
$pvSources = @((Join-Path $pvRoot 'native\pv_fbx.c'), (Join-Path $pvRoot 'vendor\ufbx\ufbx.c'))
& $Compiler cc -target x86_64-windows-gnu -shared -std=c99 -O2 -DNDEBUG -s "-I$pvRoot\vendor\ufbx" "-I$pvRoot\vendor\stb" $pvSources -o "$pvDist\pv-fbx.dll"
if ($LASTEXITCODE -ne 0) { throw 'FBX module compilation failed' }
Copy-Item -LiteralPath "$pvRoot\vendor\ufbx\LICENSE" -Destination "$pvDist\LICENSE-ufbx.txt" -Force
Copy-Item -LiteralPath "$pvRoot\vendor\stb\LICENSE" -Destination "$pvDist\LICENSE-stb.txt" -Force
