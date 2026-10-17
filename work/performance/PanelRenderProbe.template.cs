using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnturnedCommandAutocomplete;

// Executes the exact old/new RenderPanel bodies against recording widgets.
// This checks emitted UI state and writes, not Unity layout, fonts, or native input.
struct Vector2 { public float x, y; public Vector2(float a, float b) { x = a; y = b; } }
struct Color
{
    float r, g, b, a;
    public Color(float red, float green, float blue, float alpha) { r=red;g=green;b=blue;a=alpha; }
    public override string ToString() { return r+","+g+","+b+","+a; }
}
class Widget
{
    public int Writes;
    bool visible;
    string text, tooltip;
    float sizeX, sizeY, positionX, positionY, scaleX, scaleY;
    Color textColor, background;
    public Widget Parent;
    public Vector2 ParentSize = new Vector2(1920,1080);
    public bool IsVisible { get {return visible;} set {Writes++;visible=value;} }
    public string Text { get {return text;} set {Writes++;text=value;} }
    public string TooltipText { get {return tooltip;} set {Writes++;tooltip=value;} }
    public float SizeOffset_X { get{return sizeX;} set{Writes++;sizeX=value;} }
    public float SizeOffset_Y { get{return sizeY;} set{Writes++;sizeY=value;} }
    public float PositionOffset_X { get{return positionX;} set{Writes++;positionX=value;} }
    public float PositionOffset_Y { get{return positionY;} set{Writes++;positionY=value;} }
    public float PositionScale_X { get{return scaleX;} set{Writes++;scaleX=value;} }
    public float PositionScale_Y { get{return scaleY;} set{Writes++;scaleY=value;} }
    public float SizeScale_Y = 0;
    public Color TextColor { get{return textColor;} set{Writes++;textColor=value;} }
    public Color BackgroundColor { get{return background;} set{Writes++;background=value;} }
    public Vector2 GetAbsoluteSize() {return ParentSize;}
    public string Snapshot()
    {
        return string.Join("|",new object[]{visible,text,tooltip,sizeX,sizeY,positionX,positionY,scaleX,scaleY,textColor,background});
    }
}
class Rect { public Vector2 size; }
class Transform { public Rect rect = new Rect(); }
abstract class Renderer
{
    public readonly PanelPlacement placement = new PanelPlacement();
    public readonly CompletionSession session = new CompletionSession();
    public Widget attached = new Widget {Parent = new Widget(),Text="/give 6",PositionOffset_X=50,PositionOffset_Y=100,SizeOffset_Y=30};
    protected Widget panel=new Widget(), hint=new Widget(), footer=new Widget(), dragHandle=new Widget(), pinButton=new Widget();
    protected Widget[] rows=Enumerable.Range(0,8).Select(i=>new Widget()).ToArray();
    public Transform parentTransform;
    protected CompletionResult paintedResult;
    protected int paintedSelection=-2,paintedStart=-1,paintedCount=-1;
    protected bool? paintedPin;
    protected void HandlePointer(Vector2 parentSize) { }
    public abstract void Render();
    public string Snapshot() {return string.Join("\n",new[]{panel,hint,footer,dragHandle,pinButton}.Concat(rows).Select(w=>w.Snapshot()));}
    public int Writes {get{return new[]{panel,hint,footer,dragHandle,pinButton}.Concat(rows).Sum(w=>w.Writes);}}
    public void Reattach()
    {
        panel=new Widget();hint=new Widget();footer=new Widget();dragHandle=new Widget();pinButton=new Widget();
        rows=Enumerable.Range(0,8).Select(i=>new Widget()).ToArray();
        paintedResult=null;paintedSelection=-2;paintedStart=paintedCount=-1;paintedPin=null;
    }
}
class LegacyRenderer : Renderer
{
// LEGACY_RENDER_BODY
}
class OptimizedRenderer : Renderer
{
// OPTIMIZED_RENDER_BODY
}
class PanelRenderProbe
{
    static int states;
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);}
    static void Equal(Renderer a,Renderer b,string name)
    {
        a.Render();b.Render();states++;
        Check(a.Snapshot()==b.Snapshot(),"Rendered state differs: "+name);
    }
    static CompletionResult Result(int count)
    {
        var result=new CompletionResult {Hint="hint",Total=count};
        for(int i=0;i<Math.Min(256,count);i++)result.Rows.Add(new Suggestion(Kind.Item,i.ToString(),"物品 · Row "+i,""));
        return result;
    }
    static void Main()
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        Renderer old=new LegacyRenderer(),next=new OptimizedRenderer();
        old.session.Result=next.session.Result=Result(5000);
        Equal(old,next,"initial unselected candidates");
        int oldBefore=old.Writes,newBefore=next.Writes;
        for(int i=0;i<120;i++)Equal(old,next,"unchanged frame");
        Console.WriteLine("SteadyFrames=120; OldWidgetWrites="+(old.Writes-oldBefore)+"; NewWidgetWrites="+(next.Writes-newBefore));
        Check(next.Writes==newBefore,"Unchanged candidates still write widgets every frame");
        foreach(int selected in new[]{0,1,7,8,15,16,255,0,-1})
        {
            old.session.Selected=next.session.Selected=selected;Equal(old,next,"selection/page "+selected);
        }
        old.placement.BeginDrag(70,150);next.placement.BeginDrag(70,150);
        old.placement.MoveDrag(350,800);next.placement.MoveDrag(350,800);Equal(old,next,"drag near bottom");
        old.placement.EndDrag();next.placement.EndDrag();
        old.placement.TogglePin();next.placement.TogglePin();Equal(old,next,"pin without new result");
        foreach(var size in new[]{new Vector2(600,300),new Vector2(240,150),new Vector2(1920,1080)})
        {
            old.attached.Parent.ParentSize=next.attached.Parent.ParentSize=size;Equal(old,next,"pinned resize");
        }
        old.placement.TogglePin();next.placement.TogglePin();Equal(old,next,"unlock");
        foreach(int count in new[]{1,0,9,256,6000})
        {
            old.session.Result=next.session.Result=Result(count);old.session.Selected=next.session.Selected=-1;
            Equal(old,next,"result change "+count);
        }
        old.attached.Text=next.attached.Text="normal chat";Equal(old,next,"ordinary chat hides panel");
        old.attached.Text=next.attached.Text="/give 6";Equal(old,next,"show same candidates again");
        old.attached.Parent.ParentSize=next.attached.Parent.ParentSize=new Vector2(0,0);Equal(old,next,"zero-size parent");
        old.attached.Parent.ParentSize=next.attached.Parent.ParentSize=new Vector2(1920,1080);Equal(old,next,"restore parent");
        old.Reattach();next.Reattach();Equal(old,next,"new chat field attachment");
        old.parentTransform=new Transform();next.parentTransform=new Transform();
        old.parentTransform.rect.size=next.parentTransform.rect.size=new Vector2(1280,720);
        Equal(old,next,"cached parent transform");
        Console.WriteLine("PANEL: "+states+" old/new rendered-state comparisons passed; Unity runtime not launched.");
    }
}
