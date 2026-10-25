using Godot;
using System;
using System.Collections.Generic;

public partial class CatCrowd : Node2D
{
    [Signal]public delegate void CatInteractedEventHandler(int catId);
    int _catCount=100;
    [Export(PropertyHint.Range,"0,500,1")]public int CatCount{get=>_catCount;set{_catCount=Math.Clamp(value,0,500);if(IsNodeReady())SyncCount();}}
    [Export]public Rect2 SpawnRect{get;set;}=new(70,100,1000,360);
    [Export(PropertyHint.Range,"6,32,1")]public float CatRadius{get;set;}=16;
    [Export(PropertyHint.Range,"1,30,1")]public float BehaviorHz{get;set;}=5;
    [Export]public CrowdCatBehavior? BehaviorTree{get;set;}
    [Export(PropertyHint.Range,"1,100,1")]public int DecisionsPerFrame{get;set;}=16;
    [Export]public bool InteractionEnabled{get;set;}=true;
    [Export(PropertyHint.Range,"1,100,1")]public float DragStrength{get;set;}=24;
    public List<CrowdCatBody> Cats{get;}=new();
    SpriteFrames _frames=null!;
    int _nextId,_cursor;
    float _clock;
    readonly Dictionary<ulong,float> _lastTick=new();
    public CrowdCatBody? Selected{get;private set;}
    Vector2 _dragOffset;
    public override void _Ready()
    {
        _frames=new SpriteFrames();_frames.RemoveAnimation("default");
        string[] animations=["idle","walk","react","drag"];
        var atlas=GD.Load<Texture2D>("res://assetsimagecat/crowd_cat_atlas.svg");
        for(int row=0;row<animations.Length;row++){
            string animation=animations[row];_frames.AddAnimation(animation);_frames.SetAnimationSpeed(animation,animation=="walk"?8:4);
            for(int frame=0;frame<4;frame++)_frames.AddFrame(animation,new AtlasTexture{Atlas=atlas,Region=new Rect2(frame*48,row*48,48,48)});
        }
        BehaviorTree??=GD.Load<CrowdCatBehavior>("res://assetsimagecat/crowd_behavior.tres");SyncCount();
    }
    void SyncCount()
    {
        while(Cats.Count>CatCount){
            var cat=Cats[^1];Cats.RemoveAt(Cats.Count-1);if(cat==Selected)Selected=null;
            _lastTick.Remove(cat.GetInstanceId());RemoveChild(cat);cat.QueueFree();
        }
        int columns=Math.Max(1,(int)MathF.Floor(SpawnRect.Size.X/(CatRadius*2.2f)));
        while(Cats.Count<CatCount){
            var cat=new CrowdCatBody();cat.Setup(_nextId,_frames,CatRadius);cat.Name=$"Cat_{_nextId}";
            int index=Cats.Count;cat.Position=SpawnRect.Position+new Vector2(CatRadius+index%columns*CatRadius*2.2f,CatRadius+index/columns*CatRadius*2.2f);
            _nextId++;AddChild(cat);Cats.Add(cat);_lastTick[cat.GetInstanceId()]=_clock;
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        _clock+=(float)delta;
        if(IsInstanceValid(Selected)){
            var target=GetGlobalMousePosition()+_dragOffset;Selected!.Sleeping=false;
            Selected.ApplyCentralForce(((target-Selected.GlobalPosition)*DragStrength-Selected.LinearVelocity*8).LimitLength(2400));
        }
        for(int unused=0;unused<Math.Min(DecisionsPerFrame,Cats.Count);unused++){
            _cursor%=Cats.Count;var cat=Cats[_cursor++];ulong id=cat.GetInstanceId();float elapsed=_clock-_lastTick[id];
            if(elapsed>=1/BehaviorHz){BehaviorTree!.Tick(cat,elapsed);_lastTick[id]=_clock;}
        }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(!InteractionEnabled){ReleaseSelected();return;}
        if(input is not InputEventMouseButton mouse)return;
        if(mouse.ButtonIndex==MouseButton.Left&&!mouse.Pressed){ReleaseSelected();return;}
        if(!mouse.Pressed||mouse.ButtonIndex is not(MouseButton.Left or MouseButton.Right))return;
        var point=GetLocalMousePosition();
        for(int i=Cats.Count-1;i>=0;i--){
            var cat=Cats[i];if(cat.Position.DistanceSquaredTo(point)>CatRadius*CatRadius)continue;
            if(mouse.ButtonIndex==MouseButton.Left){ReleaseSelected();Selected=cat;_dragOffset=cat.GlobalPosition-GetGlobalMousePosition();cat.Dragged=true;cat.SetState("drag",0);}
            else cat.Interact();
            EmitSignal(SignalName.CatInteracted,cat.CatId);GetViewport().SetInputAsHandled();break;
        }
    }
    public void ReleaseSelected()
    {
        if(IsInstanceValid(Selected)){Selected!.Dragged=false;Selected.DecisionLeft=0.3f;Selected.SetState("idle",0);}Selected=null;
    }
    public override void _Notification(int what){if(what==NotificationWMWindowFocusOut)ReleaseSelected();}
}
