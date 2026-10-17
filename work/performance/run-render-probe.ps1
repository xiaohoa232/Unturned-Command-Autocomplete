$ErrorActionPreference = 'Stop'
$topicRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Read-RenderBody($path) {
    $source = [IO.File]::ReadAllText($path)
    $start = $source.IndexOf('private void RenderPanel()')
    $end = $source.IndexOf('private void HandlePointer(', $start)
    if ($start -lt 0 -or $end -lt 0) { throw '无法定位原始候选框绘制方法。' }
    return $source.Substring($start, $end - $start).Replace('private void RenderPanel()', 'public override void Render()')
}
$legacy = Read-RenderBody (Join-Path $PSScriptRoot 'Module-0.1.2.cs')
$current = Read-RenderBody (Join-Path $topicRoot 'src\Module.cs')
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PanelRenderProbe.template.cs'))
$programSource = $template.Replace('// LEGACY_RENDER_BODY', $legacy).Replace('// OPTIMIZED_RENDER_BODY', $current)
$generated = Join-Path $PSScriptRoot 'PanelRenderProbe.generated.cs'
[IO.File]::WriteAllText($generated, $programSource, (New-Object Text.UTF8Encoding($false)))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$program = Join-Path $PSScriptRoot 'PanelRenderProbe.exe'
& $compiler /nologo /optimize+ /target:exe ('/out:' + $program) (Join-Path $topicRoot 'src\CompletionCore.cs') (Join-Path $topicRoot 'src\PanelPlacement.cs') $generated
if ($LASTEXITCODE -ne 0) { throw '界面状态对照程序编译失败。' }
& $program | Tee-Object -FilePath (Join-Path $PSScriptRoot 'render-parity.txt')
if ($LASTEXITCODE -ne 0) { throw '界面状态对照失败。' }
