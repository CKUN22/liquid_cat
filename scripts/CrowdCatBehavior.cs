using Godot;
using System;

[GlobalClass]
public partial class CrowdCatBehavior : Resource
{
    [Export(PropertyHint.Range,"0.2,10,0.1")] public float IdleSeconds{get;set;}=2.5f;
    [Export(PropertyHint.Range,"0.2,10,0.1")] public float WanderSeconds{get;set;}=1.2f;
    [Export(PropertyHint.Range,"0,200,1")] public float WanderSpeed{get;set;}=28;
    public void Tick(CrowdCatBody cat,float delta)
    {
        if(cat.Dragged){cat.SetState("drag",0);return;}
        if(cat.ReactionLeft>0){cat.ReactionLeft=Math.Max(0,cat.ReactionLeft-delta);cat.SetState("react",0);return;}
        cat.DecisionLeft-=delta;
        if(cat.DecisionLeft<=0){
            if(cat.State=="walk"){cat.DecisionLeft=IdleSeconds*cat.Rng.RandfRange(0.7f,1.3f);cat.SetState("idle",0);}
            else{cat.DecisionLeft=WanderSeconds*cat.Rng.RandfRange(0.7f,1.3f);cat.SetState("walk",WanderSpeed*(cat.Rng.Randf()<0.5f?-1:1));}
        }
        if(cat.State=="walk")cat.ApplyCentralImpulse(new Vector2(Math.Clamp(cat.TargetSpeed-cat.LinearVelocity.X,-12,12)*cat.Mass,0));
    }
}
