<#
.SYNOPSIS
    发布任务栏日历到 Publish。

.DESCRIPTION
    默认发布自包含的 win-x64 单文件版本，目标机器不需要安装 .NET 运行时。
    发布前会清空输出目录。

.PARAMETER Configuration
    生成配置，默认 Release。

.PARAMETER Runtime
    目标运行时，默认 win-x64。

.PARAMETER NoSelfContained
    改为依赖框架发布（目标机器需要安装 .NET 10 桌面运行时）。

.EXAMPLE
    pwsh -File publish.ps1
    pwsh -File publish.ps1 -NoSelfContained
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$NoSelfContained
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "TaskbarCalendar\TaskbarCalendar.csproj"
$output = Join-Path $root "Publish"

if (-not (Test-Path $project)) {
    throw "找不到项目文件：$project"
}

if (Test-Path $output) {
    Write-Host "清理输出目录：$output"
    Remove-Item -Path $output -Recurse -Force
}

$selfContained = -not $NoSelfContained

$arguments = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContained.ToString().ToLowerInvariant(),
    "-o", $output,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

Write-Host "开始发布（$Configuration / $Runtime / 自包含=$selfContained）"
& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "发布失败，dotnet 退出码 $LASTEXITCODE"
}

$exe = Join-Path $output "TaskbarCalendar.exe"
if (-not (Test-Path $exe)) {
    throw "发布完成但没找到 $exe"
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "发布完成：$exe（$size MB）"
Write-Host "数据目录在 exe 同级 data 文件夹，首次运行会自动创建并同步时光序。"
