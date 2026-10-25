using Godot;

public partial class CatCrowdLab : Node2D
{
    Label _label=null!;
    float _refresh;
    public override void _Ready()
    {
        GetWindow().Size=new Vector2I(1152,720);GetWindow().ContentScaleSize=new Vector2I(1152,720);
        GetWindow().ContentScaleMode=Window.ContentScaleModeEnum.CanvasItems;RenderingServer.SetDefaultClearColor(new Color("192936"));
        Rect2[] rects=[new(0,650,1152,70),new(-30,0,30,720),new(1152,0,30,720),new(0,-30,1152,30),new(210,550,210,24),new(720,490,210,24)];
        foreach(var rect in rects){
            var body=new StaticBody2D{Position=rect.GetCenter()};body.AddChild(new CollisionShape2D{Shape=new RectangleShape2D{Size=rect.Size}});AddChild(body);
            AddChild(new Polygon2D{Polygon=LiquidCatLab.RectPolygon(rect),Color=new Color("3a5360")});
        }
        var canvas=new CanvasLayer();AddChild(canvas);_label=new Label{Position=new Vector2(24,16)};_label.AddThemeFontSizeOverride("font_size",20);canvas.AddChild(_label);
        canvas.AddChild(new Label{Position=new Vector2(24,680),Text="LMB drag / RMB hop | CatCrowd Inspector: Cat Count / Behavior Hz / Behavior Tree"});
    }
    public override void _Process(double delta)
    {
        _refresh-=(float)delta;if(_refresh>0)return;_refresh=0.5f;var crowd=GetNode<CatCrowd>("CatCrowd");int asleep=0;
        foreach(var cat in crowd.Cats)if(cat.Sleeping)asleep++;
        _label.Text=$"INTERACTIVE CAT CROWD   {crowd.Cats.Count} cats   {Engine.GetFramesPerSecond()} FPS   {asleep} asleep";
    }
}
