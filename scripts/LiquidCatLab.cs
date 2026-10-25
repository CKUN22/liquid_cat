using Godot;
using System;
using System.Collections.Generic;
using GArray = Godot.Collections.Array<Godot.Vector2[]>;

public partial class LiquidCatLab : Node2D
{
    [Export(PropertyHint.Range,"0,6,1")] public int InitialLayout { get; set; }
    [Export] public bool InitialWater { get; set; }
    public int Layout { get; private set; }
    public GArray Terrain { get; private set; } = new();
    LiquidCat _cat = null!;
    bool _press, _paused;
    float _pressTime;
    Font _font = null!;
    readonly List<Rect2> _buttons = new();
    readonly Rect2 _waterButton = new(281,45,94,17);
    static readonly Color Bg = new("192936"), Panel = new("223846"), Edge = new("54717c"), Mint = new("a3dbc1"), White = new("f6edda"), Muted = new("91a9af");
    static readonly string[] Names = ["SLOPE","GAP","PRESS","POOL","FALLS","FUNNEL","VESSELS"];
    static readonly string[] Descriptions = ["SLOPE + STEPS","21 PX CHANNEL + CATCH BASIN","CONTAINER + MOVING PRESS [P]","STILL WATER / SPACE MAKES A WAVE","THREE CASCADES + COLLECTION POOL","FUNNEL + 28 PX THROAT","CONNECTED VESSELS / 23 PX BOTTOM PASSAGE"];
    static readonly Vector2[] Spawns = [new(95,140),new(95,140),new(232,120),new(232,135),new(73,112),new(240,118),new(180,156)];
    public override void _Ready()
    {
        var window = GetWindow(); window.ContentScaleSize = new Vector2I(480,300);
        window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        window.ContentScaleStretch = Window.ContentScaleStretchEnum.Integer;
        window.Size = new Vector2I(1152,720); RenderingServer.SetDefaultClearColor(Bg);
        _font = GD.Load<Font>("res://assets/fonts/unifont-16.0.04.ttf");
        _cat = GetNode<LiquidCat>("LiquidCat");
        for (int i=0;i<7;i++) _buttons.Add(new Rect2(228+i*34,12,31,22));
        _cat.SetWaterMode(InitialWater); SetLayout(InitialLayout);
    }
    public static Vector2[] RectPolygon(Rect2 r) => [r.Position,r.Position+new Vector2(r.Size.X,0),r.End,r.Position+new Vector2(0,r.Size.Y)];
    public static GArray BuildTerrain(int layout, bool press = false, float pressTime = 0)
    {
        var terrain = new GArray { RectPolygon(new Rect2(0,258,480,42)), RectPolygon(new Rect2(0,78,10,190)), RectPolygon(new Rect2(470,78,10,190)) };
        void Rect(float x,float y,float w,float h) => terrain.Add(RectPolygon(new Rect2(x,y,w,h)));
        switch (layout) {
            case 0:
                terrain.Add([new(20,210),new(160,210),new(221,253),new(20,258)]);
                Rect(326,220,30,38);Rect(356,195,30,63);Rect(386,170,76,88);break;
            case 1: Rect(20,211,185,47);Rect(172,78,33,112);Rect(288,212,15,46);Rect(430,212,15,46);break;
            case 2: Rect(138,173,16,85);Rect(326,173,16,85);Rect(154,244,172,14);break;
            case 3: Rect(138,166,14,92);Rect(328,166,14,92);Rect(152,251,176,7);break;
            case 4:
                terrain.Add([new(20,153),new(120,153),new(160,176),new(20,176)]);
                terrain.Add([new(163,195),new(242,195),new(275,216),new(163,216)]);
                Rect(300,229,14,29);Rect(444,205,14,53);break;
            case 5:
                terrain.Add([new(70,114),new(226,205),new(226,235),new(70,235)]);
                terrain.Add([new(410,114),new(410,235),new(254,235),new(254,205)]);break;
            case 6: Rect(118,148,14,110);Rect(348,148,14,110);Rect(231,110,18,125);break;
        }
        if (press) { float y=102+(1-MathF.Cos(pressTime*1.1f))*0.5f*134; Rect(162,y-12,156,12); }
        return terrain;
    }
    public void SetLayout(int index)
    {
        Layout=Math.Clamp(index,0,6);_press=false;_pressTime=0;
        _cat.Reset(Spawns[Layout]); UpdateTerrain();
    }
    void UpdateTerrain() { Terrain=BuildTerrain(Layout,_press,_pressTime);_cat.SetObstacles(Terrain); }
    public override void _PhysicsProcess(double delta)
    {
        if (!_paused && _press) { _pressTime+=(float)delta;UpdateTerrain(); }
        QueueRedraw();
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed:true,Echo:false } key) {
            switch(key.PhysicalKeycode) {
                case Key.Key1: SetLayout(0);break;case Key.Key2:SetLayout(1);break;case Key.Key3:SetLayout(2);break;
                case Key.Key4:SetLayout(3);break;case Key.Key5:SetLayout(4);break;case Key.Key6:SetLayout(5);break;case Key.Key7:SetLayout(6);break;
                case Key.R:SetLayout(Layout);break;
                case Key.P:_press=!_press;_pressTime=0;UpdateTerrain();break;
                case Key.Tab:_paused=!_paused;_cat.SimulationEnabled=!_paused;break;
            }
        }
        if (input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left}) {
            var mouse=GetGlobalMousePosition();
            for(int i=0;i<_buttons.Count;i++) if(_buttons[i].HasPoint(mouse)) SetLayout(i);
            if(_waterButton.HasPoint(mouse)) _cat.SetWaterMode(!_cat.IsWater);
        }
    }
    void Text(Vector2 at,string text,Color? color=null,int size=10) => DrawString(_font,at,text,HorizontalAlignment.Left,-1,size,color??White);
    public override void _Draw()
    {
        if (_font==null) return;
        DrawRect(new Rect2(0,0,480,300),Bg);
        for(int x=16;x<480;x+=16)for(int y=90;y<258;y+=16)DrawRect(new Rect2(x,y,1,1),new Color("2a414b"));
        DrawRect(new Rect2(0,0,480,71),Panel);DrawLine(new(16,70),new(464,70),Edge);
        DrawRect(new Rect2(16,15,3,17),Mint);Text(new(26,23),"LIQUID / CAT",White,16);
        Text(new(27,36),"WHITE CAT / "+(_cat.IsWater?"WATER":"COHESIVE FLUID"),Muted,7);
        for(int i=0;i<_buttons.Count;i++) {
            DrawRect(_buttons[i],Layout==i?Mint:new Color("304b58"));
            Text(_buttons[i].Position+new Vector2(3,9),(i+1).ToString(),Layout==i?Bg:White,7);
            Text(_buttons[i].Position+new Vector2(2,18),Names[i][..Math.Min(5,Names[i].Length)],Layout==i?Bg:White,6);
        }
        Text(new(20,57),"WHITE CAT",White,8);
        DrawRect(_waterButton,_cat.IsWater?new Color("74d4e8"):new Color("304550"));
        Text(_waterButton.Position+new Vector2(6,12),"F  WATER "+(_cat.IsWater?"ON":"OFF"),_cat.IsWater?Bg:White,8);
        for(int i=0;i<Terrain.Count;i++) {
            var polygon=Terrain[i];DrawColoredPolygon(polygon,new Color("3a5360"));
            var outline=new Vector2[polygon.Length+1];polygon.CopyTo(outline,0);outline[^1]=polygon[0];DrawPolyline(outline,Edge,1);
            if(i>2)DrawLine(polygon[0],polygon[1],Mint,2);
        }
        if(_press)Text(new(365,82),"PRESS / ACTIVE",new Color("e9a6b2"),8);
        Text(new(18,82),Descriptions[Layout],Muted,8);
        if(Layout==3||Layout==6){Text(new(18,105),"F: water state",new Color("74d4e8"),8);Text(new(18,117),"SPACE: splash",Muted,8);}
        DrawRect(new Rect2(0,268,480,32),Panel);DrawLine(new(16,268),new(464,268),Edge);
        Text(new(16,281),"AD move  W hop/swim  S crouch/dive  SPACE splash/hop",White,9);
        Text(new(16,294),"1-7 room  R reset  P press  V particles  E gather  TAB pause",Muted,8);
        Text(new(393,294),$"{_cat.ParticleCount:00} / {(_paused?"PAUSE":"LIVE")}",Mint,8);
    }
}
