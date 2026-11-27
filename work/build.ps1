param(
    [Parameter(Mandatory = $true)][string]$GamePath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $GamePath 'Unturned_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到本机 C# 编译器。' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'outputs' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$stage = Join-Path $output ('.build-' + [Guid]::NewGuid().ToString('N'))
$stagedModules = Join-Path $stage 'Modules'
$stagedModule = Join-Path $stagedModules 'CommandAutocomplete'
$dll = Join-Path $stagedModule 'CommandAutocomplete.dll'
$manifestPath = Join-Path $stagedModule 'CommandAutocomplete.module'
try {
    [IO.Directory]::CreateDirectory($stagedModule) | Out-Null
    $refs = @('Assembly-CSharp.dll','SDG.Glazier.Runtime.dll','UnturnedDat.dll','UnityEngine.CoreModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.UIElementsModule.dll','UnityEngine.TextRenderingModule.dll','Unity.TextMeshPro.dll','com.rlabrecque.steamworks.net.dll','netstandard.dll')
    $arguments = @('/nologo','/target:library','/optimize+','/warn:4','/warnaserror+',('/out:' + $dll))
    foreach ($reference in $refs) { $arguments += '/reference:' + (Join-Path $managed $reference) }
    foreach ($source in @('CompletionCore.cs','PanelPlacement.cs','CandidateViewport.cs','Module.cs')) {
        $arguments += Join-Path $projectRoot ('src\' + $source)
    }
    & $compiler $arguments
    if ($LASTEXITCODE -ne 0) { throw '模组编译失败。' }

    $version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
    $manifest = @{
        IsEnabled = $true
        Name = 'CommandAutocomplete'
        Version = $version.ToString()
        Assemblies = @(@{ Path = '/CommandAutocomplete.dll'; Role = 'Client' })
        Dependencies = @()
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
    $versionLabel = if ($version.Revision -eq 0) { $version.ToString(3) } else { $version.ToString() }
    $name = 'CommandAutocomplete-' + $versionLabel
    $destination = Join-Path $output $name
    try {
        [IO.Directory]::Move($stage, $destination)
    } catch [IO.IOException] {
        if (!(Test-Path -LiteralPath $destination)) { throw }
        $destination = Join-Path $output ($name + '-重建-' + [Guid]::NewGuid().ToString('N'))
        [IO.Directory]::Move($stage, $destination)
    }
    Write-Output ('构建成功：' + (Join-Path $destination 'Modules\CommandAutocomplete'))
} finally {
    if (Test-Path -LiteralPath $stage) {
        foreach ($file in @($dll, $manifestPath)) { [IO.File]::Delete($file) }
        foreach ($directory in @($stagedModule, $stagedModules, $stage)) {
            if ([IO.Directory]::Exists($directory)) { [IO.Directory]::Delete($directory) }
        }
    }
}
