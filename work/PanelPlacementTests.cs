using System;
using UnturnedCommandAutocomplete;

class PanelPlacementTests
{
    static int count;
    static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        count++; Console.WriteLine("PASS " + name);
    }
    static bool Near(float actual, float expected) { return Math.Abs(actual - expected) < 0.01f; }
    static void Main()
    {
        var p = new PanelPlacement();
        Check(!p.Pinned && !p.HasCustomPosition && !p.Dragging, "startup is unlocked and follows chat");
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 100) && Near(p.Y, 238), "default aligns below chat at its left edge");
        p.Place(1920, 1080, 500, 144, 120, 258);
        Check(Near(p.X, 120) && Near(p.Y, 258), "default follows animated chat position");
        Check(p.BeginDrag(170, 278), "unlocked title can begin drag");
        p.MoveDrag(470, 428);
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 420) && Near(p.Y, 408), "drag preserves mouse grab offset without jumping");
        p.EndDrag();
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 420) && Near(p.Y, 408) && !p.Dragging, "chat close and reopen retains position");
        p.TogglePin();
        Check(p.Pinned && !p.BeginDrag(440, 420), "pin rejects mouse drag");
        p.MoveDrag(900, 700);
        p.Place(1920, 1080, 500, 144, 200, 300);
        Check(Near(p.X, 420) && Near(p.Y, 408), "pinned position survives cursor and chat movement");
        p.TogglePin();
        Check(!p.Pinned && p.BeginDrag(440, 420), "second pin click re-enables dragging");
        p.MoveDrag(-1000, -1000);
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 0) && Near(p.Y, 0), "drag cannot lose title off top or left edge");
        p.MoveDrag(4000, 4000);
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 1420) && Near(p.Y, 936), "drag keeps panel width and one row inside bottom and right edge");
        p.TogglePin();
        Check(p.Pinned && !p.Dragging, "pin cancels an active drag");
        p.Place(1280, 720, 500, 144, 100, 238);
        Check(Near(p.X, 780) && Near(p.Y, 576), "smaller window keeps pinned controls reachable");
        p.Place(320, 200, 320, 144, 100, 238);
        Check(Near(p.X, 0) && Near(p.Y, 56), "narrow viewport is bounded");
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 1420) && Near(p.Y, 936), "restoring the window restores the pinned coordinates");
        p.TogglePin();
        p.Place(320, 200, 320, 144, 100, 238);
        p.Place(1920, 1080, 500, 144, 100, 238);
        Check(Near(p.X, 1420) && Near(p.Y, 936), "temporary clamping also preserves unpinned custom coordinates");
        var pinnedDefault = new PanelPlacement();
        pinnedDefault.Place(1280, 720, 500, 144, 100, 238);
        pinnedDefault.TogglePin();
        pinnedDefault.Place(1280, 720, 500, 144, 300, 438);
        Check(Near(pinnedDefault.X, 100) && Near(pinnedDefault.Y, 238), "pin freezes default position before first drag");
        pinnedDefault.TogglePin();
        Check(pinnedDefault.BeginDrag(120, 250), "unlock of default position permits drag");
        pinnedDefault.EndDrag();
        pinnedDefault.MoveDrag(900, 600);
        Check(Near(pinnedDefault.X, 100) && Near(pinnedDefault.Y, 238), "hidden or detached panel cannot continue a canceled drag");
        var restarted = new PanelPlacement();
        restarted.Place(1280, 720, 500, 144, 100, 238);
        Check(!restarted.Pinned && Near(restarted.X, 100) && Near(restarted.Y, 238), "new game run resets position and pin");
        CheckViewport();
        Console.WriteLine("Passed " + count + " panel placement and viewport checks; no game was launched.");
    }

    static void CheckViewport()
    {
        var view = new CandidateViewport();
        view.Resize(12, 4);
        Check(view.CanScroll && view.VisibleCount == 4 && view.First == 0, "first candidate window uses available capacity");
        int initial = view.Revision;
        view.ScrollWheel(-0.5f);
        Check(view.First == 0 && view.Revision == initial, "partial wheel movement keeps the visible rows");
        view.ScrollWheel(-0.5f);
        Check(view.First == 1 && view.Revision == initial + 1, "two partial notches scroll one row");
        view.ScrollWheel(-30f);
        Check(view.First == 8, "wheel is clamped at the last page");
        view.ScrollWheel(1f);
        Check(view.First == 7, "wheel reverses immediately at the boundary");
        view.Reveal(2);
        Check(view.First == 2, "keyboard selection above the page becomes visible");
        view.Reveal(11);
        Check(view.First == 8, "keyboard selection below the page becomes visible");
        view.Resize(12, 8);
        Check(view.First == 4 && view.VisibleCount == 8, "larger capacity clamps the start to the last valid page");
        view.Resize(12, 4);
        const float track = 120f;
        float thumb = view.ThumbHeight(track);
        float start = view.ThumbTop(track);
        view.BeginDrag(start + thumb / 2, track);
        Check(view.Dragging && view.First == 4, "grabbing the thumb retains its current position");
        view.MoveDrag(track, track);
        Check(view.First == 8, "dragging the thumb reaches the last page");
        view.EndPointerInteraction();
        view.MoveDrag(0f, track);
        Check(!view.Dragging && view.First == 8, "released scrollbar does not follow the pointer");
        view.BeginDrag(1f, track);
        Check(view.First == 0 && view.Dragging, "clicking the track moves to the chosen page");
        view.Resize(3, 4);
        Check(!view.CanScroll && !view.Dragging && view.First == 0, "a list that fits cancels scrolling and dragging");
        view.Resize(12, 0);
        view.Reveal(11);
        view.BeginDrag(0f, 0f);
        Check(!view.CanScroll && !view.Dragging && view.First == 0, "zero capacity safely disables interaction");
        view.Resize(12, 4);
        view.BeginDrag(1f, 8f);
        Check(!view.Dragging && view.First == 0, "track with no thumb travel cannot start dragging");
    }
}
