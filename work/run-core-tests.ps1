param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'test-results')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testOutput = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$testExe = Join-Path $testOutput 'CoreTests.exe'
& $compiler /nologo /target:exe ('/out:' + $testExe) (Join-Path $projectRoot 'src\CompletionCore.cs') (Join-Path $PSScriptRoot 'CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw '补全规则测试编译失败。' }
& $testExe | Tee-Object -FilePath (Join-Path $testOutput 'core-tests.txt')
if ($LASTEXITCODE -ne 0) { throw '补全规则测试失败。' }
