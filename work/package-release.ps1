param(
    [Parameter(Mandatory = $true)][string]$ModuleDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'outputs' }
$module = Get-Item -LiteralPath $ModuleDirectory
if (!$module.PSIsContainer -or ($module.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw '请选择一个实际构建目录。'
}
$dll = Join-Path $module.FullName 'CommandAutocomplete.dll'
$manifestPath = Join-Path $module.FullName 'CommandAutocomplete.module'
$config = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$assembly = [Reflection.AssemblyName]::GetAssemblyName($dll)
$version = $assembly.Version
if ($assembly.Name -ne 'CommandAutocomplete' -or $config.Name -ne $assembly.Name -or
    $config.Version -ne $version.ToString() -or $config.IsEnabled -ne $true -or
    @($config.Assemblies).Count -ne 1 -or $config.Assemblies[0].Role -ne 'Client' -or
    $config.Assemblies[0].Path -ne '/CommandAutocomplete.dll' -or @($config.Dependencies).Count -ne 0) {
    throw '程序集和模块清单不一致。'
}

$notices = $projectRoot
$files = [ordered]@{
    'Modules/CommandAutocomplete/CommandAutocomplete.dll' = $dll
    'Modules/CommandAutocomplete/CommandAutocomplete.module' = $manifestPath
    'LICENSE' = Join-Path $notices 'LICENSE'
    'THIRD_PARTY_NOTICES.md' = Join-Path $notices 'THIRD_PARTY_NOTICES.md'
    '使用说明.md' = Join-Path $PSScriptRoot '手动安装说明.md'
}
$hashes = @{}
function Get-StreamSha256([IO.Stream]$stream) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha256.ComputeHash($stream)) }
    finally { $sha256.Dispose() }
}
foreach ($entry in $files.Keys) {
    $file = Get-Item -LiteralPath $files[$entry]
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw ('不能打包链接或目录：' + $file.FullName)
    }
    $stream = [IO.File]::OpenRead($file.FullName)
    try { $hashes[$entry] = Get-StreamSha256 $stream }
    finally { $stream.Dispose() }
}

$output = New-Item -ItemType Directory -Path $OutputDirectory -Force
$name = 'Unturned-Command-Autocomplete-' + $version.ToString(3) + '.zip'
$destination = Join-Path $output.FullName $name
if (Test-Path -LiteralPath $destination) { throw ('已有安装包：' + $destination) }
$temp = Join-Path $output.FullName ('.' + [Guid]::NewGuid() + '.zip')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $archive = [IO.Compression.ZipFile]::Open($temp, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $files.Keys) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $files[$entry], $entry) | Out-Null
        }
    } finally { $archive.Dispose() }
    $archive = [IO.Compression.ZipFile]::OpenRead($temp)
    try {
        if (($archive.Entries.FullName -join '|') -cne ($files.Keys -join '|')) { throw 'ZIP 文件清单不一致。' }
        foreach ($entry in $archive.Entries) {
            $stream = $entry.Open()
            try { $hash = Get-StreamSha256 $stream }
            finally { $stream.Dispose() }
            if ($hash -ne $hashes[$entry.FullName]) { throw ('ZIP 文件校验失败：' + $entry.FullName) }
        }
    } finally { $archive.Dispose() }
    [IO.File]::Move($temp, $destination)
    Get-Item -LiteralPath $destination
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
}
