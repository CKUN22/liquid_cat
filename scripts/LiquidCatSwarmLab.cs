using Godot;
using System;
using GArray=Godot.Collections.Array<Godot.Vector2[]>;

public partial class LiquidCatSwarmLab : Node2D
{
    int _catCount=100;
    [Export(PropertyHint.Range,"0,200,1")] public int CatCount {get=>_catCount;set{_catCount=Math.Clamp(value,0,200);if(IsNodeReady())Swarm.CatCount=_catCount;}}
    bool _merge=true,_water,_autonomous;
    [Export] public bool MergeOnContact {get=>_merge;set{_merge=value;if(IsNodeReady())Swarm.MergeOnContact=value;}}
    [Export] public bool WaterMode {get=>_water;set{_water=value;if(IsNodeReady())Swarm.WaterMode=value;}}
    [Export] public bool AutonomousBehavior {get=>_autonomous;set{_autonomous=value;if(IsNodeReady())Swarm.AutonomousBehavior=value;}}
    public LiquidCatSwarm Swarm {get;private set;}=null!;
    public int Layout {get;private set;}
    public GArray Terrain {get;private set;}=new();
    float _time,_timer;
    Label _label=null!;
    static readonly string[] Names=["PILE","28 PX FUNNEL","23 PX CONNECTED VESSELS","MOVING PRESS","CASCADES"];
    public override void _Ready()
    {
        GetWindow().Size=new Vector2I(1280,900);GetWindow().ContentScaleSize=new Vector2I(1280,900);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.CanvasItems;
        RenderingServer.SetDefaultClearColor(new Color("192936"));
        Swarm=GetNode<LiquidCatSwarm>("LiquidCatSwarm");
        Swarm.CatCount=CatCount;Swarm.MergeOnContact=MergeOnContact;Swarm.WaterMode=WaterMode;Swarm.AutonomousBehavior=AutonomousBehavior;
        var canvas=new CanvasLayer();AddChild(canvas);
        _label=new Label{Position=new Vector2(16,12)};_label.AddThemeFontSizeOverride("font_size",16);canvas.AddChild(_label);
        SetLayout(0);
    }
    public void SetLayout(int index)
    {
        Layout=Math.Clamp(index,0,4);_time=0;UpdateTerrain();Swarm.ResetPopulation();
        int count=Swarm.Metrics()["cats"].AsInt32();
        for(int i=0;i<Math.Min(CatCount,count);i++){
            var origin=new Vector2(80+i%17*66,125+i/17*60);
            if(Layout==1)origin=new Vector2(180+i%14*66,120+i/14*55);
            else if(Layout==2)origin=new Vector2(110+i%8*60,120+i/8*48);
            Swarm.ResetCat(i,origin);
        }
        QueueRedraw();
    }
    void UpdateTerrain()
    {
        Terrain=new GArray{LiquidCatLab.RectPolygon(new Rect2(0,850,1280,50)),LiquidCatLab.RectPolygon(new Rect2(0,78,10,800)),LiquidCatLab.RectPolygon(new Rect2(1270,78,10,800))};
        switch(Layout){
            case 1:Terrain.Add([new(10,420),new(626,660),new(626,700),new(10,700)]);Terrain.Add([new(1270,420),new(1270,700),new(654,700),new(654,660)]);break;
            case 2:Terrain.Add(LiquidCatLab.RectPolygon(new Rect2(630,250,20,577)));break;
            case 3:float y=400+(1-MathF.Cos(_time*0.7f))*0.5f*410;Terrain.Add(LiquidCatLab.RectPolygon(new Rect2(30,y,520,16)));break;
            case 4:Terrain.Add([new(10,400),new(340,400),new(430,450),new(10,450)]);Terrain.Add([new(440,610),new(760,610),new(850,660),new(440,660)]);break;
        }
        Swarm.SetObstacles(Terrain);
    }
    public override void _PhysicsProcess(double delta)
    {
        if(Swarm.SimulationEnabled)_time+=(float)delta;
        if(Layout==3&&Swarm.SimulationEnabled){UpdateTerrain();QueueRedraw();}
    }
    public override void _Process(double delta)
    {
        _timer-=(float)delta;if(_timer>0)return;_timer=0.5f;
        var m=Swarm.Metrics();
        _label.Text=$"LIQUID CATS / {Names[Layout]} / {m["cats"]} bodies / volume {m["mass_units"]} / {m["particles"]} particles / {Engine.GetFramesPerSecond()} FPS\nPhysics {m["physics_ms"].AsDouble():F2} ms / Raster {m["raster_ms"].AsDouble():F2} ms / Upload {m["upload_ms"].AsDouble():F2} ms\n1-5 terrain | R reset | T all water | M merge/reset | TAB pause | LMB select/drag | RMB splash/hop\nSelected cat: AD move, WS swim/crouch, SPACE splash/hop, E gather, F water";
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(input is not InputEventKey{Pressed:true,Echo:false} key)return;
        switch(key.PhysicalKeycode){
            case Key.Key1:SetLayout(0);break;case Key.Key2:SetLayout(1);break;case Key.Key3:SetLayout(2);break;case Key.Key4:SetLayout(3);break;case Key.Key5:SetLayout(4);break;
            case Key.R:SetLayout(Layout);break;case Key.M:MergeOnContact=!MergeOnContact;SetLayout(Layout);break;
            case Key.T:WaterMode=!WaterMode;break;case Key.Tab:Swarm.SimulationEnabled=!Swarm.SimulationEnabled;break;
        }
    }
    public override void _Draw()
    {
        foreach(var polygon in Terrain){
            DrawColoredPolygon(polygon,new Color("3a5360"));var outline=new Vector2[polygon.Length+1];polygon.CopyTo(outline,0);outline[^1]=polygon[0];DrawPolyline(outline,new Color("a3dbc1"),1);
        }
    }
}
