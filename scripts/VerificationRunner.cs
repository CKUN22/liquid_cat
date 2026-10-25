using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GArray = Godot.Collections.Array<Godot.Vector2[]>;

// Run with: Godot --headless --path . res://scenes/verification.tscn
public partial class VerificationRunner : Node
{
    readonly List<string> _failures=new();
    public override void _Ready(){ProcessMode=ProcessModeEnum.Always;Callable.From(Run).CallDeferred();}
    void Check(bool ok,string message){if(!ok){_failures.Add(message);GD.PushError(message);}}
    async void Run()
    {
        try{
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(OS.GetCmdlineUserArgs().Contains("--capture")){await Capture();GetTree().Quit();return;}
            await VerifyInventory();
            VerifyLiquid();VerifyMerge();await VerifyCrowd();VerifyScenes();
            GD.Print(_failures.Count==0?"ALL C# CHECKS PASSED":"C# CHECKS FAILED: "+string.Join("; ",_failures));
            GetTree().Quit(_failures.Count==0?0:1);
        }catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    static InputEventKey KeyEvent(Key key)=>new(){PhysicalKeycode=key,Keycode=key,Pressed=true};
    async System.Threading.Tasks.Task VerifyInventory()
    {
        var inventory=GetNode<Inventory>("/root/Inventory");Check(inventory.Slots.Count==24,"Inventory capacity");
        Input.ParseInputEvent(KeyEvent(Key.B));await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        Check(inventory.IsOpen&&GetTree().Paused,"B opens and pauses");
        Input.ParseInputEvent(KeyEvent(Key.B));await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        Check(!inventory.IsOpen&&!GetTree().Paused,"B closes and resumes");
        inventory.SetOpen(true);Input.ParseInputEvent(KeyEvent(Key.Escape));await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        Check(!inventory.IsOpen&&!GetTree().Paused,"Esc closes");
        GetTree().Paused=true;inventory.SetOpen(true);inventory.SetOpen(false);Check(GetTree().Paused,"Preserve pause");GetTree().Paused=false;
        var item=new InventoryItem{Id="test",MaxStack=5,DisplayName="测试物品"};
        Check(inventory.AddItem(item,8)==8&&inventory.Slots[0].Count==5&&inventory.Slots[1].Count==3,"Inventory stacking");
        inventory.MoveItem(0,2);Check(inventory.Slots[0].Item==null&&inventory.Slots[2].Count==5,"Inventory move");
        inventory.RemoveItem(2,2);inventory.MoveItem(1,2);Check(inventory.Slots[2].Count==5&&inventory.Slots[1].Count==1,"Inventory merge overflow");
        Check(inventory.AddItem(item,1000)==114,"Inventory full remainder");inventory.MoveItem(2,2);Check(inventory.Slots[2].Count==5,"Inventory self-drop");
        var other=new InventoryItem{Id="other",MaxStack=1};inventory.RemoveItem(0,5);inventory.AddItem(other);
        inventory.MoveItem(0,2);Check(inventory.Slots[2].Item==other&&inventory.Slots[0].Item==item,"Inventory swaps different items");
        var slot=new InventorySlot{OwnerInventory=inventory,SlotIndex=0};
        var data=new Godot.Collections.Dictionary{["inventory"]=inventory,["slot"]=2};
        Check(slot._CanDropData(Vector2.Zero,data),"Inventory drag target accepts valid data");
        Check(!slot._CanDropData(Vector2.Zero,42),"Inventory rejects unrelated drag data");
        slot.Free();
        foreach(var stack in inventory.Slots){stack.Item=null;stack.Count=0;}
        inventory.SetOpen(true);inventory.SetOpen(false);
        GD.Print("INVENTORY C# PASS");
    }
    LiquidCatSwarm CreateSwarm(int count=1,Vector2I? world=null)
    {
        var swarm=new LiquidCatSwarm{CatCount=count,WorldSize=world??new Vector2I(480,300),KeyboardControl=false,UseThreads=false,RenderEnabled=false};
        AddChild(swarm);swarm.SetPhysicsProcess(false);swarm.SetProcess(false);return swarm;
    }
    void Valid(LiquidCatSwarm swarm,string label)
    {
        int count=swarm.Metrics()["cats"].AsInt32();
        for(int i=0;i<count;i++){
            var state=swarm.Snapshot(i);Check(state["finite"].AsBool(),label+" finite");Check(state["penetrating"].AsInt32()==0,label+" terrain");
            Check(state["particles"].AsInt32()==85,label+" 85 particles");
        }
    }
    void VerifyLiquid()
    {
        var swarm=CreateSwarm();
        Vector2[] spawns=[new(95,140),new(95,140),new(232,120),new(232,135),new(73,112),new(240,118),new(180,156)];
        for(int layout=0;layout<7;layout++)foreach(bool water in new[]{false,true}){
            swarm.SetObstacles(LiquidCatLab.BuildTerrain(layout));swarm.ResetCat(0,spawns[layout]);swarm.SetCatWater(0,water);
            for(int tick=0;tick<30;tick++)swarm.Advance(1f/60);
            Valid(swarm,$"room {layout} water {water}");
            var before=swarm.Snapshot(0);swarm.RenderNow();var after=swarm.Snapshot(0);
            Check(before["points"].AsVector2Array().SequenceEqual(after["points"].AsVector2Array()),"Render preserves mass positions");
            Check(before["velocities"].AsVector2Array().SequenceEqual(after["velocities"].AsVector2Array()),"Render preserves velocities");
            swarm.SetCatWater(0,!water);Check(swarm.Snapshot(0)["points"].AsVector2Array().SequenceEqual(after["points"].AsVector2Array()),"Toggle preserves positions");
        }
        swarm.SetObstacles(LiquidCatLab.BuildTerrain(5));swarm.ResetCat(0,new Vector2(240,118));swarm.SetCatWater(0,true);
        for(int i=0;i<600;i++)swarm.Advance(1f/60);
        int through=swarm.Snapshot(0)["points"].AsVector2Array().Count(p=>p.Y>237);Check(through>40,"Funnel flow");Valid(swarm,"funnel");
        swarm.SetObstacles(LiquidCatLab.BuildTerrain(3));swarm.ResetCat(0,new Vector2(232,135));
        for(int i=0;i<360;i++)swarm.Advance(1f/60);
        var points=swarm.Snapshot(0)["points"].AsVector2Array();Check(points.Max(p=>p.X)-points.Min(p=>p.X)>65,"Water spreads");
        swarm.JumpCat(0);swarm.Advance(1f/60);Check(swarm.Snapshot(0)["velocities"].AsVector2Array().Any(v=>v.Y< -10),"Water splash");
        swarm.SetCatGather(0,true);for(int i=0;i<60;i++)swarm.Advance(1f/60);Valid(swarm,"gather");swarm.SetCatGather(0,false);
        swarm.SetObstacles(new GArray{LiquidCatLab.RectPolygon(new Rect2(1,250,478,49))});swarm.ResetCat(0,new Vector2(240,160));
        Vector2 lastAnchor=Vector2.Zero,lastCenter=swarm.Snapshot(0)["center"].AsVector2();bool previous=false;int jumps=0,visible=0;
        for(int tick=0;tick<240;tick++){
            swarm.Advance(1f/60);swarm.RenderNow();var state=swarm.Snapshot(0);bool supported=state["face_supported"].AsBool();
            var center=state["center"].AsVector2();var anchor=state["face_anchor"].AsVector2();
            if(supported){visible++;if(previous&&(anchor-lastAnchor-(center-lastCenter)).Length()>4)jumps++;}
            lastAnchor=anchor;lastCenter=center;previous=supported;
        }
        Check(jumps==0&&visible>200,"Water facial tracking");swarm.Free();
        var crowd=CreateSwarm(100,new Vector2I(1280,900));crowd.UseThreads=true;crowd.RenderEnabled=true;
        crowd.SetObstacles(new GArray{LiquidCatLab.RectPolygon(new Rect2(0,850,1280,50))});
        var samples=new List<double>();
        for(int tick=0;tick<120;tick++){ulong start=Time.GetTicksUsec();crowd.Advance(1f/60);if(tick>=60)samples.Add((Time.GetTicksUsec()-start)/1000.0);}
        Valid(crowd,"100 cats");Check(crowd.ParticleCount==8500,"100 cats mass");
        foreach(int count in new[]{24,0,100}){crowd.CatCount=count;crowd.Advance(1f/60);Check(crowd.Metrics()["cats"].AsInt32()==count,"Swarm count "+count);}
        samples.Sort();GD.Print($"LIQUID C# PASS funnel={through}/85 face_visible={visible}/240 jumps={jumps}; 100-cat CPU p50={samples[samples.Count/2]:F3}ms p95={samples[(int)(samples.Count*0.95)]:F3}ms");crowd.Free();
    }
    void VerifyMerge()
    {
        var swarm=CreateSwarm(2,new Vector2I(1280,900));swarm.Gravity=0;swarm.MergeOnContact=true;
        swarm.ResetCat(0,new Vector2(200,200));swarm.ResetCat(1,new Vector2(400,200));swarm.Advance(1f/60);Check(swarm.Metrics()["cats"].AsInt32()==2,"Separated cats");
        swarm.ResetCat(1,new Vector2(248,200));for(int i=0;i<8;i++)swarm.Advance(1f/60);
        Check(swarm.Metrics()["cats"].AsInt32()==1&&swarm.Metrics()["mass_units"].AsInt32()==2&&swarm.ParticleCount==85,"Merge mass and budget");
        Check(Math.Abs(swarm.Snapshot(0)["body_scale"].AsDouble()*swarm.Snapshot(0)["body_scale"].AsDouble()-2)<0.0001,"Merge scale");
        swarm.RenderEnabled=true;for(int i=0;i<120;i++)swarm.Advance(1f/60);Check(swarm.Snapshot(0)["face_supported"].AsBool(),"Merged face");
        swarm.ResetPopulation();var wall=new GArray{LiquidCatLab.RectPolygon(new Rect2(224,80,4,770))};swarm.SetObstacles(wall);
        swarm.ResetCat(0,new Vector2(195,200));swarm.ResetCat(1,new Vector2(257,200));for(int i=0;i<30;i++)swarm.Advance(1f/60);
        Check(swarm.Metrics()["cats"].AsInt32()==2,"No merging across walls");
        swarm.CatCount=3;swarm.Advance(1f/60);swarm.SetObstacles(new GArray());
        for(int i=0;i<3;i++)swarm.ResetCat(i,new Vector2(300+i*45,250));for(int i=0;i<20;i++)swarm.Advance(1f/60);
        Check(swarm.Metrics()["cats"].AsInt32()==1&&swarm.Metrics()["mass_units"].AsInt32()==3,"Chain merge");
        swarm.CatCount=0;swarm.Advance(1f/60);Check(swarm.ParticleCount==0,"Merged zero count");
        swarm.CatCount=100;swarm.Gravity=260;swarm.UseThreads=true;swarm.ResetPopulation();swarm.SetObstacles(new GArray{LiquidCatLab.RectPolygon(new Rect2(0,850,1280,50))});
        for(int i=0;i<600;i++)swarm.Advance(1f/60);Check(swarm.Metrics()["mass_units"].AsInt32()==100&&swarm.ParticleCount<8500,"100-cat merged mass");Valid(swarm,"merged pile");
        swarm.WaterMode=true;for(int i=0;i<180;i++)swarm.Advance(1f/60);Valid(swarm,"merged water");Check(swarm.Metrics()["mass_units"].AsInt32()==100,"Merged water mass");
        GD.Print("MERGE C# PASS ",Json.Stringify(swarm.Metrics()));swarm.Free();
    }
    async System.Threading.Tasks.Task VerifyCrowd()
    {
        var lab=GD.Load<PackedScene>("res://scenes/cat_crowd_lab.tscn").Instantiate<CatCrowdLab>();AddChild(lab);
        var crowd=lab.GetNode<CatCrowd>("CatCrowd");crowd.CatCount=100;Check(crowd.Cats.Count==100,"Crowd count");
        Check(crowd.Cats[0].Visual.SpriteFrames==crowd.Cats[99].Visual.SpriteFrames,"Shared atlas");
        var cat=crowd.Cats[0];cat.Interact();Check(cat.State=="react","Crowd reacts");cat.Dragged=true;crowd.BehaviorTree!.Tick(cat,0.2f);Check(cat.State=="drag","Drag priority");cat.Dragged=false;
        crowd.CatCount=24;Check(crowd.Cats.Count==24,"Crowd 24");crowd.CatCount=0;Check(crowd.Cats.Count==0,"Crowd zero");
        await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);crowd.CatCount=100;
        for(int i=0;i<120;i++)await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
        foreach(var body in crowd.Cats)Check(body.Position.IsFinite()&&body.Position.Y<680,"Crowd contained");
        GD.Print("RIGID CROWD C# PASS");lab.Free();
    }
    void VerifyScenes()
    {
        foreach(string path in new[]{"liquid_cat_lab","water_cat_pool","water_cat_vessels","cat_cascades","cat_funnel","liquid_cat_swarm_lab","benchmark_cat_merge"}){
            var node=GD.Load<PackedScene>($"res://scenes/{path}.tscn").Instantiate();
            Check(node.GetScript().As<Script>().ResourcePath.EndsWith(".cs"),"Scene C# "+path);
            if(path!="benchmark_cat_merge") {
                AddChild(node); node.SetPhysicsProcess(false); node.SetProcess(false);
                if(node is LiquidCatLab room) room.SetLayout(room.InitialLayout);
                if(node is LiquidCatSwarmLab lab) lab.SetLayout(1);
            }
            node.Free();
        }
        GD.Print("C# SCENE REFERENCES PASS");
    }
    async System.Threading.Tasks.Task Capture()
    {
        var lab=GD.Load<PackedScene>("res://scenes/liquid_cat_swarm_lab.tscn").Instantiate<LiquidCatSwarmLab>();lab.CatCount=8;AddChild(lab);
        lab.SetPhysicsProcess(false);lab.SetProcess(false);lab.Swarm.SetPhysicsProcess(false);lab.Swarm.SetProcess(false);
        for(int i=0;i<90;i++)lab.Swarm.Advance(1f/60);
        GetNode<Inventory>("/root/Inventory").SetOpen(true);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("Z:/Godot/liquid_cat/gpt/all-csharp-preview.png");
        GD.Print("C# PREVIEW SAVED");
    }
}
