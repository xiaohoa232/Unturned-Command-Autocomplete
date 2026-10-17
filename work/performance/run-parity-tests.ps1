param([switch]$SkipPerformance)
$ErrorActionPreference = 'Stop'
$topicRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$program = Join-Path $PSScriptRoot 'SearchParityTests.exe'
& $compiler /nologo /optimize+ /target:exe ('/out:' + $program) (Join-Path $topicRoot 'src\CompletionCore.cs') (Join-Path $PSScriptRoot 'CompletionCore-0.1.2.cs') (Join-Path $PSScriptRoot 'SearchParityTests.cs')
if ($LASTEXITCODE -ne 0) { throw '新旧搜索对照程序编译失败。' }
$testArgs = @()
if ($SkipPerformance) { $testArgs += '--parity-only' }
$logName = if ($SkipPerformance) { 'parity-final.txt' } else { 'parity-and-performance.txt' }
& $program @testArgs | Tee-Object -FilePath (Join-Path $PSScriptRoot $logName)
if ($LASTEXITCODE -ne 0) { throw '新旧搜索对照检查失败。' }
