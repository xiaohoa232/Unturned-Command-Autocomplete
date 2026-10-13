param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $GamePath 'Unturned_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到本机 C# 编译器。' }
$outDir = Join-Path $projectRoot 'outputs\CommandAutocomplete-0.1.2\Modules\CommandAutocomplete'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$refs = @('Assembly-CSharp.dll','SDG.Glazier.Runtime.dll','UnturnedDat.dll','UnityEngine.CoreModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.UIElementsModule.dll','UnityEngine.TextRenderingModule.dll','com.rlabrecque.steamworks.net.dll','netstandard.dll')
$arguments = @('/nologo','/target:library','/optimize+','/warn:4',('/out:' + (Join-Path $outDir 'CommandAutocomplete.dll')))
foreach ($reference in $refs) { $arguments += '/reference:' + (Join-Path $managed $reference) }
$arguments += @((Join-Path $projectRoot 'src\CompletionCore.cs'),(Join-Path $projectRoot 'src\PanelPlacement.cs'),(Join-Path $projectRoot 'src\Module.cs'))
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw '模组编译失败。' }
$manifest = @{ IsEnabled = $true; Name = 'CommandAutocomplete'; Version = '0.1.2.0'; Assemblies = @(@{Path='/CommandAutocomplete.dll';Role='Client'}); Dependencies = @() }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outDir 'CommandAutocomplete.module') -Encoding utf8
Write-Output ('构建成功：' + $outDir)
