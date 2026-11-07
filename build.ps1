$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\WinNote\WinNote.csproj'
$dist = Join-Path $root 'dist'

Write-Host '正在发布 WinNote（自包含单文件）...'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $dist

Copy-Item (Join-Path $dist 'WinNote.exe') (Join-Path $root 'WinNote.exe') -Force
Write-Host "已生成：$(Join-Path $root 'WinNote.exe')"
