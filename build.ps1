param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts')
)

$ErrorActionPreference = 'Stop'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '找不到 Windows 自带的 .NET Framework C# 编译器。'
}

$source = Join-Path $PSScriptRoot 'src\HermesProxyBridge.cs'
if (-not (Test-Path -LiteralPath $source)) { throw "找不到源码：$source" }
$manifest = Join-Path $PSScriptRoot 'src\HermesProxyBridge.exe.manifest'
$background = Join-Path $PSScriptRoot 'assets\cat-ui-overlay-v2.png'
if (-not (Test-Path -LiteralPath $manifest)) { throw "找不到 DPI 清单：$manifest" }
if (-not (Test-Path -LiteralPath $background)) { throw "找不到猫咪背景资源：$background" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$target = Join-Path $OutputDirectory 'HermesProxyBridge.exe'
$manifestArg = "/win32manifest:$manifest"
$resourceArg = "/resource:$background,HermesProxyBridge.CatBackground"

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    /out:$target $manifestArg $resourceArg `
    /reference:System.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll $source
if ($LASTEXITCODE) { throw "csc 编译失败：$LASTEXITCODE" }
Write-Host "Built $target"
