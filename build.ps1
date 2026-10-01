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
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$target = Join-Path $OutputDirectory 'HermesProxyBridge.exe'

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    /out:$target /reference:System.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll $source
if ($LASTEXITCODE) { throw "csc 编译失败：$LASTEXITCODE" }
Write-Host "Built $target"
