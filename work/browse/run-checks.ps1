$ErrorActionPreference = 'Stop'
$topic = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$src = Join-Path $topic 'src'
$oldWork = Join-Path $topic 'work'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$core = Join-Path $src 'CompletionCore.cs'
$placement = Join-Path $src 'PanelPlacement.cs'

function Run-Check([string]$name, [string[]]$sources, [string[]]$testArgs = @()) {
    $exe = Join-Path $PSScriptRoot ($name + '.exe')
    & $compiler /nologo /optimize+ /target:exe ('/out:' + $exe) @sources
    if ($LASTEXITCODE -ne 0) { throw ('Compile failed: ' + $name) }
    & $exe @testArgs | Tee-Object -FilePath (Join-Path $PSScriptRoot ($name + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw ('Check failed: ' + $name) }
}


function Read-Range([string]$source, [string]$first, [string]$next) {
    $start = $source.IndexOf($first, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw ('Method missing: ' + $first) }
    $end = $source.IndexOf($next, $start + $first.Length, [StringComparison]::Ordinal)
    if ($end -lt 0) { throw ('End marker missing: ' + $next) }
    return $source.Substring($start, $end - $start)
}

$current = [IO.File]::ReadAllText((Join-Path $src 'Module.cs'))
$render = [IO.File]::ReadAllText((Join-Path $oldWork 'performance\PanelRenderProbe.template.cs'))
$render = $render.Substring(0, $render.IndexOf('class LegacyRenderer'))
$render = $render -replace '(?m)^class Widget(?=\r?\n)', 'class Widget : ISleekElement'
$render = $render.Replace('public Widget Parent;', @'
public Widget Parent;
    public int MaxLength = 200, FocusCalls;
    public void FocusControl() { FocusCalls++; }
    public Vector2 Cursor = new Vector2(-1, -1);
    public Vector2 GetNormalizedCursorPosition() { return Cursor; }
'@)
$render = $render.Replace('protected void HandlePointer(Vector2 parentSize) { }', 'protected void HandlePointer(float x, float y) { }')
$probe = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'BrowseProbe.template.cs'))
$probe = $probe.Replace('// ROW_CONSTANTS', (Read-Range $current 'private const float RowLeft' 'private CompletionEngine engine'))
$probe = $probe.Replace('// RENDER_METHODS', (Read-Range $current 'private void RenderPanel()' 'private void HandlePointer(').Replace('private void RenderPanel()', 'public override void Render()'))
$click = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ClickProbe.template.cs'))
$click = $click.Replace('public static bool down, up;', 'public static bool down, up, held; public static Vector2 mouseScrollDelta; public static bool GetMouseButton(int button) { return button == 0 && held; }')
$click = $click.Replace('pendingCaretUntil, pressedStart;', 'pendingCaretUntil, pressedStart, pressedViewRevision, lastTabFrame = -1;')
$click = $click.Replace('r.FooterText.Contains("左键点选"), "footer advertises mouse selection"', 'r.FooterText.Contains("显示 1–8 / 17"), "footer shows the displayed range"')
$click = $click.Replace('r.Render(); Down(r); Up(r, 1);', 'r.Render(); Down(r); Up(r, 7);')
$click = $click.Replace('"/give 1009" && r.session.Selected == 9, "second-page click uses the displayed page offset"', '"/give 1008" && r.session.Selected == 8, "click after Tab scroll uses the displayed range offset"')
$click = $click.Replace('for (int i = 0; i < 7; i++) { Time.frameCount++; r.Tab(false); }', 'r = New(Engine(1));')
$click = $click.Replace('r.attached.Text == "/give 1016", "hidden row cannot be selected on a short final page"', 'r.attached.Text == "/give ", "hidden row cannot be selected in a short result"')
$click = $click.Replace('// CLICK_HANDLERS', (Read-Range $current 'private bool ApplyTab(' '// Prevent IMGUI'))
$click = $click.Replace('// REFOCUS_HANDLER', (Read-Range $current 'private void RefocusChat(' 'private void RefocusSafely('))
$files = @()
$utf8 = New-Object Text.UTF8Encoding($true)
foreach ($pair in @(@{Name='Render';Source=$render},@{Name='Click';Source=$click},@{Name='Browse';Source=$probe})) {
    $file = Join-Path $PSScriptRoot ($pair.Name + 'Probe.generated.cs')
    [IO.File]::WriteAllText($file, $pair.Source, $utf8)
    $files += $file
}
Run-Check 'browse-and-click' (@($core, $placement, (Join-Path $src 'CandidateViewport.cs')) + $files)
