using System;
using System.Globalization;
using UnturnedCommandAutocomplete;

static class Application { public static bool isFocused = true; }

class TextMeshProUGUI
{
    public Margin margin = new Margin { x = 5, z = 5 };
    public struct Margin { public float x, z; }
    public Vector2 GetPreferredValues(string value, float width, float height)
    {
        return new Vector2(value.Length * 9, 18);
    }
}

partial class OptimizedRenderer : Renderer
{
    // ROW_CONSTANTS
    private readonly CandidateViewport viewport = new CandidateViewport();
    private readonly Widget track = new Widget(), thumb = new Widget();
    private readonly TextMeshProUGUI[] rowText = new TextMeshProUGUI[8];
    private int hoveredRow = -1;
    private float paintedRowWidth = -1;

    public OptimizedRenderer()
    {
        for (int i = 0; i < rowText.Length; i++) rowText[i] = new TextMeshProUGUI();
    }

    public void PointAt(float x, float y)
    {
        Vector2 size = parentTransform == null ? attached.Parent.ParentSize : parentTransform.rect.size;
        attached.Parent.Cursor = new Vector2((placement.X + x) / size.x, (placement.Y + y) / size.y);
    }
    public int First { get { return viewport.First; } }
    public int Hovered { get { return hoveredRow; } }
    public bool DraggingThumb { get { return viewport.Dragging; } }
    public Widget Row(int i) { return rows[i]; }
    public Widget Track { get { return track; } }
    public Widget Thumb { get { return thumb; } }
    // RENDER_METHODS
}

static class BrowseProbe
{
    static int checks;
    const string LongLabel = "A deliberately long candidate name with <size=70>literal markup</size> and further descriptive text";
    static void Check(bool pass, string title)
    {
        if (!pass) throw new Exception("BROWSE FAIL: " + title);
        checks++;
        Console.WriteLine("BROWSE PASS: " + title);
    }

    static CompletionEngine MakeEngine()
    {
        var engine = new CompletionEngine();
        engine.Commands.Add(new CommandSpec("give", "give", "give", ""));
        for (int i = 0; i < 42; i++)
            engine.Catalog[Kind.Item].Add(new Suggestion(Kind.Item, (1000 + i).ToString(), "Item " + i.ToString("D2") + (i == 8 ? " " + LongLabel : ""), ""));
        return engine;
    }

    static void Wheel(OptimizedRenderer r, float notches)
    {
        Time.frameCount++;
        Input.mouseScrollDelta = new Vector2(0, notches);
        r.Render();
        Input.mouseScrollDelta = new Vector2();
    }

    static void Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        ClickProbe.Run();
        var engine = MakeEngine();
        var r = new OptimizedRenderer();
        PlayerLifeUI.chatting = true; PlayerLifeUI.chatField = r.attached;
        r.attached.Text = "/give ";
        r.session.Observe(engine, r.attached.Text); r.Render();
        Check(r.First == 0 && r.Track.IsVisible && r.Thumb.IsVisible && r.Row(0).Text.Contains("Item 00") &&
            r.FooterText == "显示 1–8 / 42  ·  滚轮浏览", "first range and scrollbar are visible");
        int steady = r.Writes + r.Track.Writes + r.Thumb.Writes;
        for (int i = 0; i < 120; i++) r.Render();
        Check(r.Writes + r.Track.Writes + r.Thumb.Writes == steady, "120 idle frames cause no widget writes");
        r.PointAt(30, 42); Wheel(r, -3);
        Check(r.First == 0 && r.Hovered == -1, "wheel outside the candidate area is ignored");
        r.PointAt(30, 80); Wheel(r, -1);
        Check(r.First == 1 && r.Hovered == 0 && r.Row(0).Text.Contains("Item 01") &&
            r.session.Selected == -1 && r.attached.Text == "/give ", "one notch scrolls one row without filling or selecting");
        Check(string.IsNullOrEmpty(r.Row(0).TooltipText) && r.Row(7).TooltipText.Replace("\u200B", "").Contains(LongLabel) &&
            r.Row(7).TooltipText.Contains("<\u200Bsize"), "short names have no tooltip and a long name appears in full with literal markup");
        Wheel(r, -0.5f); Check(r.First == 1, "partial wheel deltas accumulate");
        Wheel(r, -0.5f); Check(r.First == 2 && r.Row(6).TooltipText.Replace("\u200B", "").Contains(LongLabel) &&
            string.IsNullOrEmpty(r.Row(7).TooltipText), "scroll reassigns and clears recycled row tooltips");
        Time.frameCount++; r.Tab(false); r.Render();
        Check(r.First == 0 && r.session.Selected == 0 && r.Row(0).Text.StartsWith("▶") && r.Hovered == 0 &&
            r.attached.Text == "/give 1000", "first Tab reveals its candidate and both highlights coexist");
        Wheel(r, -100);
        Check(r.First == 34 && r.FooterText.StartsWith("显示 35–42 / 42") && r.session.Selected == 0 &&
            r.attached.Text == "/give 1000", "bottom clamps without changing the prior Tab selection");
        Time.frameCount++; r.Tab(false); r.Render();
        Check(r.First == 1 && r.session.Selected == 1 && r.attached.Text == "/give 1001",
            "Tab resumes the original cycle from a distant scroll position");
        Wheel(r, 100);
        Check(r.First == 0 && r.FooterText.StartsWith("显示 1–8 / 42"), "top clamps without wrapping");

        Time.frameCount++; Input.down = true; r.Capture(); Input.down = false;
        Wheel(r, -4); Wheel(r, 4);
        Time.frameCount++; Input.up = true; r.Click(2); Input.up = false;
        Check(r.attached.Text == "/give 1001", "scrolling away and back invalidates the held click");
        Wheel(r, -8);
        Time.frameCount++; Input.down = true; r.Capture(); Input.down = false;
        Time.frameCount++; Input.up = true; r.Click(2); Input.up = false;
        Check(r.attached.Text == "/give 1010" && r.session.Selected == 10 && r.attached.FocusCalls > 0,
            "a fresh click fills the visible scrolled candidate and returns focus");

        r.Render();
        float thumbPosition = r.Thumb.PositionOffset_Y;
        r.PointAt(486, thumbPosition + 4);
        Time.frameCount++; Input.down = Input.held = true; r.Render(); Input.down = false;
        Check(r.DraggingThumb && r.First == 8 && r.Hovered == -1, "grabbing the thumb keeps its position and clears row hover");
        r.PointAt(510, 420); r.Render();
        Check(r.First == 34 && r.Thumb.PositionOffset_Y + r.Thumb.SizeOffset_Y == 276 && r.session.Selected == 10,
            "thumb drag follows the pointer outside the track and leaves the selection alone");
        Input.held = false; Input.up = true; r.Render(); Input.up = false;
        Check(!r.DraggingThumb && r.attached.Text == "/give 1010", "releasing the thumb does not fill or send");
        r.placement.TogglePin(); r.PointAt(486, 72);
        Time.frameCount++; Input.down = Input.held = true; r.Render(); Input.down = Input.held = false; r.Render();
        Check(r.First == 0 && r.placement.Pinned && !r.DraggingThumb, "track click jumps and works while the panel is pinned");

        r.PointAt(30, 80); Wheel(r, -19);
        r.attached.Text = "/give 1041"; r.session.Observe(engine, r.attached.Text); r.Render();
        Check(r.First == 0 && !r.Track.IsVisible && !r.Thumb.IsVisible && !r.Row(1).IsVisible &&
            r.FooterText == "显示 1–1 / 1", "editing restarts at the top and hides the bar when all rows fit");
        r.attached.Parent.ParentSize = new Vector2(110, 1080); r.Render();
        Check(r.Row(0).TooltipText.Contains("Item 41"), "narrower geometry rechecks tooltip overflow");
        r.attached.Text = "normal chat"; r.Render();
        Check(r.Hovered == -1 && !r.DraggingThumb, "leaving command mode clears hover and pointer drag");

        r.attached.Parent.ParentSize = new Vector2(1920, 1080);
        r.attached.Text = "/give ";
        r.session.Result = new CompletionResult { Total = 500, Hint = "Many matches" };
        for (int i = 0; i < 256; i++) r.session.Result.Rows.Add(new Suggestion(Kind.Item, i.ToString(), "Candidate " + i, ""));
        r.Render(); r.PointAt(30, 80); Wheel(r, -600);
        Check(r.First == 248 && r.FooterText == "显示 249–256 / 500  ·  仅前 256 项",
            "visible range distinguishes loaded rows from the total matches");
        r.session.Selected = 255; r.Render();
        r.parentTransform = new Transform();
        r.parentTransform.rect.size = new Vector2(640, 300);
        r.Render();
        Check(r.First > 248 && r.Row(255 - r.First).Text.StartsWith("▶"),
            "a resize keeps a previously visible Tab selection in view");
        Console.WriteLine("BROWSE: " + checks + " checks passed using the production viewport, rendering and click code.");
    }
}
