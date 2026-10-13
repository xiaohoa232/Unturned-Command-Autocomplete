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
        Console.WriteLine("Passed " + count + " panel placement checks; no game was launched.");
    }
}
