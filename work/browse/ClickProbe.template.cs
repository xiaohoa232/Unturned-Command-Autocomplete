using System;
using UnturnedCommandAutocomplete;

// The production click and focus handlers are injected below. These fakes do not
// claim to test Unity's pointer dispatch or focus; the user will test those in game.
interface ISleekElement { }
enum EventType { MouseUp, KeyDown }
class Event { public static Event current; public EventType rawType; public int button; }
static class Input
{
    public static string compositionString = "";
    public static bool down, up;
    public static bool GetMouseButtonDown(int button) { return button == 0 && down; }
    public static bool GetMouseButtonUp(int button) { return button == 0 && up; }
}
static class Time { public static int frameCount; }
static class PlayerLifeUI { public static bool chatting; public static Widget chatField; }

partial class OptimizedRenderer
{
    int lastSelectionFrame = -1, pendingCaretUntil, pressedStart;
    string pendingCaretText;
    CompletionResult pressedResult;
    public int CaretLength = -1;
    void MoveCaretToEnd(Widget field, int length) { CaretLength = length; }
    public void Capture() { CaptureCandidatePress(); }
    public void Click(int slot) { OnCandidateClicked(rows[slot]); }
    public void Tab(bool backwards) { ApplyTab(backwards); }
    public bool CaretScheduled(string value) { return pendingCaretText == value && pendingCaretUntil == Time.frameCount + 2; }
    public string FooterText { get { return footer.Text; } }
    // CLICK_HANDLERS
    // REFOCUS_HANDLER
}

static class ClickProbe
{
    static int checks;
    static void Check(bool passed, string title)
    {
        if (!passed) throw new Exception("CLICK FAIL: " + title);
        checks++;
        Console.WriteLine("CLICK PASS: " + title);
    }

    static CompletionEngine Engine(int count)
    {
        var engine = new CompletionEngine();
        engine.Commands.Add(new CommandSpec("give", "give", "give", ""));
        for (int i = 0; i < count; i++)
            engine.Catalog[Kind.Item].Add(new Suggestion(Kind.Item, (1000 + i).ToString(), "Item " + i.ToString("D2"), ""));
        return engine;
    }

    static OptimizedRenderer New(CompletionEngine engine)
    {
        Input.down = Input.up = false; Input.compositionString = ""; Event.current = null;
        var renderer = new OptimizedRenderer();
        renderer.attached.Text = "/give ";
        PlayerLifeUI.chatting = true; PlayerLifeUI.chatField = renderer.attached;
        renderer.session.Observe(engine, renderer.attached.Text);
        renderer.Render();
        return renderer;
    }

    static void Down(OptimizedRenderer renderer)
    {
        Time.frameCount++; Input.down = true; renderer.Capture(); Input.down = false;
    }

    static void Up(OptimizedRenderer renderer, int slot)
    {
        Time.frameCount++; Input.up = true; renderer.Click(slot); Input.up = false;
    }

    public static void Run()
    {
        var engine = Engine(17);
        var r = New(engine);
        Check(r.FooterText.Contains("左键点选"), "footer advertises mouse selection");
        Down(r); Up(r, 2);
        Check(r.attached.Text == "/give 1002" && r.session.Selected == 2, "left click fills exactly the displayed candidate");
        Check(r.attached.FocusCalls == 1 && r.CaretLength == r.attached.Text.Length && r.CaretScheduled(r.attached.Text), "focus and caret return to the chat field");
        Input.up = true; r.Click(3); Input.up = false;
        Check(r.session.Selected == 2 && r.attached.FocusCalls == 1, "duplicate click in the same frame is ignored");
        Time.frameCount++; r.Tab(false);
        Check(r.attached.Text == "/give 1003", "Tab continues from the clicked candidate");
        Time.frameCount++; r.Tab(true);
        Check(r.attached.Text == "/give 1002", "Shift+Tab continues in reverse from the clicked candidate");

        r = New(engine);
        for (int i = 0; i <= 8; i++) { Time.frameCount++; r.Tab(false); }
        r.Render(); Down(r); Up(r, 1);
        Check(r.attached.Text == "/give 1009" && r.session.Selected == 9, "second-page click uses the displayed page offset");
        for (int i = 0; i < 7; i++) { Time.frameCount++; r.Tab(false); }
        r.Render(); Down(r); Up(r, 1);
        Check(r.attached.Text == "/give 1016", "hidden row cannot be selected on a short final page");

        r = New(engine); Down(r);
        Event.current = new Event { rawType = EventType.MouseUp, button = 1 }; r.Click(0);
        Event.current = new Event { rawType = EventType.KeyDown, button = 0 }; r.Click(0);
        Check(r.session.Selected == -1 && r.attached.Text == "/give ", "right release and keyboard submit do not fill");
        Event.current = new Event { rawType = EventType.MouseUp, button = 0 }; r.Click(1);
        Check(r.attached.Text == "/give 1001", "legacy left-release event fills the candidate");

        r = New(engine); Down(r); Input.compositionString = "zhong"; Up(r, 0);
        Check(r.session.Selected == -1, "IME composition blocks selection");
        Input.compositionString = ""; r.placement.BeginDrag(25, 25); Down(r); Up(r, 0);
        Check(r.session.Selected == -1, "active drag blocks selection");
        r.placement.EndDrag(); r.placement.TogglePin(); r.Render(); Down(r); Up(r, 0);
        Check(r.attached.Text == "/give 1000" && r.placement.Pinned, "pin locks position but allows a candidate click");

        r = New(engine); Down(r); r.attached.Text = "/give 10"; Up(r, 0);
        Check(r.attached.Text == "/give 10" && r.session.Selected == -1, "stale painted result cannot replace changed input");
        r = New(engine); Down(r); r.attached.Text = "/give Item";
        r.session.Observe(engine, r.attached.Text); r.Render(); Up(r, 1);
        Check(r.attached.Text == "/give Item", "a new result between press and release cancels the click");

        r = New(engine); Down(r);
        for (int i = 0; i <= 8; i++) { Time.frameCount++; r.Tab(false); }
        r.Render(); Up(r, 1);
        Check(r.attached.Text == "/give 1008", "a page change between press and release cancels the click");

        r = New(engine); r.attached.MaxLength = r.attached.Text.Length;
        Down(r); Up(r, 0); Time.frameCount++; r.Tab(false);
        Check(r.attached.Text == "/give " && r.session.Selected == -1 && r.attached.FocusCalls == 0, "oversize candidate is rejected without changing Tab state or focus");

        r = New(engine); Down(r); PlayerLifeUI.chatting = false; Up(r, 0);
        Check(r.session.Selected == -1, "closed chat cannot fill a candidate");
        r = New(engine); Down(r); PlayerLifeUI.chatField = new Widget(); Up(r, 0);
        Check(r.session.Selected == -1, "old chat field cannot fill after it is replaced");
        Console.WriteLine("CLICK: " + checks + " checks passed using production handlers and simulated input; no game launched.");
    }
}
