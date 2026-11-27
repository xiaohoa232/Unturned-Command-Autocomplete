param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'test-results'))
$ErrorActionPreference = 'Stop'
$topicRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testOutput = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$testExe = Join-Path $testOutput 'PanelPlacementTests.exe'
& $compiler /nologo /target:exe ('/out:' + $testExe) (Join-Path $topicRoot 'src\PanelPlacement.cs') (Join-Path $topicRoot 'src\CandidateViewport.cs') (Join-Path $PSScriptRoot 'PanelPlacementTests.cs')
if ($LASTEXITCODE -ne 0) { throw '候选框位置测试编译失败。' }
& $testExe | Tee-Object -FilePath (Join-Path $testOutput 'panel-tests.txt')
if ($LASTEXITCODE -ne 0) { throw '候选框位置测试失败。' }
