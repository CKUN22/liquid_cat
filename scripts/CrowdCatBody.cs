using Godot;

public partial class CrowdCatBody : RigidBody2D
{
    public int CatId {get;private set;}=-1;
    public StringName State {get;private set;}="idle";
    public float DecisionLeft,ReactionLeft,TargetSpeed;
    public bool Dragged;
    public RandomNumberGenerator Rng {get;}=new();
    public AnimatedSprite2D Visual {get;private set;}=null!;
    public void Setup(int id,SpriteFrames frames,float radius)
    {
        CatId=id;Rng.Seed=(ulong)(id*7919+37);DecisionLeft=Rng.RandfRange(0.2f,3);
        Mass=1;GravityScale=0.65f;LockRotation=true;LinearDamp=1.5f;
        PhysicsMaterialOverride=new PhysicsMaterial{Friction=0.65f,Bounce=0.05f};
        AddChild(new CollisionShape2D{Shape=new CircleShape2D{Radius=radius}});
        Visual=new AnimatedSprite2D{SpriteFrames=frames,Scale=Vector2.One*radius/16,TextureFilter=TextureFilterEnum.Nearest};
        AddChild(Visual);Visual.Play("idle");Visual.Frame=id%4;Visual.SpeedScale=Rng.RandfRange(0.85f,1.15f);
    }
    public void SetState(StringName next,float speed)
    {
        TargetSpeed=speed;if(State==next)return;State=next;Visual.Play(next);if(speed!=0)Visual.FlipH=speed<0;
    }
    public void Interact(){ReactionLeft=0.8f;Sleeping=false;SetState("react",0);ApplyCentralImpulse(new Vector2(0,-100));}
}
