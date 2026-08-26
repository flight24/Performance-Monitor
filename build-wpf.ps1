# Performance Monitor - WPF 版一键编译脚本
# 用法: .\build-wpf.ps1            # 自包含单文件(无需安装 .NET 运行时)
#       .\build-wpf.ps1 -Lite     # 轻量版(需系统安装 .NET 9 Desktop Runtime)

param(
    [switch]$Lite
)

$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "PerformanceMonitor.Wpf"
$outDir = Join-Path $PSScriptRoot "dist"

$extraArgs = @()
if (-not $Lite) {
    $extraArgs = @(
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )
}

Write-Host "== 正在编译 WPF 版性能监视器 ==" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 @extraArgs -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Host "编译失败" -ForegroundColor Red; exit 1 }

# 重命名产物（monitor.exe 与旧版命名习惯一致）
$exe = Join-Path $outDir "PerformanceMonitor.exe"
$target = Join-Path $outDir "monitor.exe"
if (-not [string]::IsNullOrEmpty($exe) -and (Test-Path $exe)) {
    Copy-Item $exe $target -Force
    Write-Host ""
    Write-Host "完成! 输出: $target" -ForegroundColor Green
    Write-Host "(需管理员权限运行以读取温度传感器)"
}
