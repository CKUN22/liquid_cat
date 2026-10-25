using Godot;
using System;
using System.Collections.Generic;

public partial class BenchmarkCatMerge : LiquidCatSwarmLab
{
    static readonly string[] Cases=["separate_pile","merged_pile","separate_water_funnel","merged_water_funnel"];
    int _phase,_minBodies=200,_maxBodies;
    double _elapsed,_warmupPeak;
    ulong _lastTick;
    bool _collecting;
    DisplayServer.VSyncMode _oldVsync;
    readonly List<double> _frames=new();
    readonly Godot.Collections.Array<Godot.Collections.Dictionary> _results=new();
    public override void _Ready()
    {
        base._Ready();_oldVsync=DisplayServer.WindowGetVsyncMode();DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);Engine.MaxFps=165;BeginCase();
    }
    void BeginCase()
    {
        _collecting=false;MergeOnContact=_phase%2==1;WaterMode=_phase>=2;SetLayout(_phase<2?0:1);
        _elapsed=0;_frames.Clear();_warmupPeak=0;_minBodies=200;_maxBodies=0;_lastTick=Time.GetTicksUsec();_collecting=true;
        GD.Print("PC_MERGE_BENCH_BEGIN ",Cases[_phase]);
    }
    public override void _Process(double delta)
    {
        base._Process(delta);if(!_collecting)return;
        ulong now=Time.GetTicksUsec();double wall=(now-_lastTick)/1000.0;_lastTick=now;
        double before=_elapsed;_elapsed+=wall/1000;
        if(before<15)_warmupPeak=Math.Max(_warmupPeak,wall);
        else{_frames.Add(wall);int bodies=Swarm.Metrics()["cats"].AsInt32();_minBodies=Math.Min(_minBodies,bodies);_maxBodies=Math.Max(_maxBodies,bodies);}
        if(_elapsed<25||_frames.Count==0)return;
        _collecting=false;double total=0;foreach(var frame in _frames)total+=frame;_frames.Sort();
        var result=new Godot.Collections.Dictionary{
            ["case"]=Cases[_phase],["average_fps"]=_frames.Count*1000/total,["p50_frame_ms"]=_frames[_frames.Count/2],
            ["p95_frame_ms"]=_frames[(int)(_frames.Count*0.95)],["sample_seconds"]=total/1000,["frames"]=_frames.Count,
            ["metrics"]=Swarm.Metrics(),["fps_cap"]=165,["warmup_peak_ms"]=_warmupPeak,["max_frame_ms"]=_frames[^1],
            ["sample_body_range"]=new int[]{_minBodies,_maxBodies}};
        _results.Add(result);GD.Print("PC_MERGE_BENCH_RESULT ",Json.Stringify(result));_phase++;
        if(_phase<Cases.Length)Callable.From(BeginCase).CallDeferred();
        else{
            using var file=FileAccess.Open("Z:/Godot/liquid_cat/gpt/pc-merge-results.json",FileAccess.ModeFlags.Write);file.StoreString(Json.Stringify(_results,"  "));
            DisplayServer.WindowSetVsyncMode(_oldVsync);MergeOnContact=true;WaterMode=false;SetLayout(0);GD.Print("PC_MERGE_BENCH_DONE");
        }
    }
}
