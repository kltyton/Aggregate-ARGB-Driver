param(
    [Parameter(Mandatory)][string]$StockPackage,
    [Parameter(Mandatory)][string]$Toolchain,
    [Parameter(Mandatory)][string]$TemporaryDirectory,
    [Parameter(Mandatory)][string]$ReportPath,
    [string]$OutputDirectory,
    [switch]$SkipPackageToolBuild
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$env:TEMP=$TemporaryDirectory;$env:TMP=$TemporaryDirectory;$env:TMPDIR=$TemporaryDirectory
if(-not (Test-Path -LiteralPath $TemporaryDirectory)){New-Item -ItemType Directory -Path $TemporaryDirectory|Out-Null}
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $root 'dist\HardwareLite.Firmware\d21x'}
if(-not (Test-Path -LiteralPath $output)){New-Item -ItemType Directory -Path $output|Out-Null}
$sources=@(
    'boot/start.S','usb/worker.c','media/storage.c','media/player.c',
    'media/frame_player.c','media/video_stream.c','display/overlay.c','display/text.c','native/memory.c'
)
$sources+='../third_party/miniz/miniz_tinfl.c'
$compiler=Join-Path $Toolchain 'bin\riscv64-unknown-elf-gcc.exe'
$arguments=@('-march=rv64imafdc','-mabi=lp64d','-mcmodel=medany','-msmall-data-limit=0','-mno-relax',
    '-Os','-Wall','-Wextra','-Werror','-ffreestanding','-fno-builtin','-fno-pic','-fno-stack-protector',
    '-ffunction-sections','-fdata-sections','-nostdlib','-Wl,--no-relax','-Wl,--gc-sections',
    '-include','native/compression.h',
    '-Wl,-T,boot/link.ld',
    '-o',([IO.Path]::GetRelativePath((Join-Path $root 'firmware/prime_slc/d21x'),(Join-Path $output 'screen.elf'))))+$sources
$previousLocation=Get-Location
try {
    Set-Location -LiteralPath (Join-Path $root 'firmware/prime_slc/d21x')
    & $compiler @arguments
    if($LASTEXITCODE -ne 0){throw 'Firmware compilation failed'}
} finally {Set-Location -LiteralPath $previousLocation.Path}
if(-not $SkipPackageToolBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'D21xPackage\D21xPackage.csproj') -c Release -v minimal
    if($LASTEXITCODE -ne 0){throw 'Firmware package tool build failed'}
}
& dotnet (Join-Path $PSScriptRoot 'D21xPackage\bin\Release\net10.0\D21xPackage.dll') $StockPackage (Join-Path $output 'screen.elf') $Toolchain (Join-Path $output 'PRIME_SLC_II_WHT_1.0.4_local.bin') $ReportPath
if($LASTEXITCODE -ne 0){throw 'Firmware packaging failed'}
