using Godot;

// Single-cat rooms share the compiled solver and renderer with the swarm.
public partial class LiquidCat : LiquidCatSwarm
{
    [Export] public CatAppearance? Appearance { get; set; }
    public override void _Ready()
    {
        CatCount = 1; WorldSize = new Vector2I(480,300);
        SpawnRect = new Rect2(66,95,60,60); MergeOnContact = false;
        base._Ready(); SelectCat(0);
        Appearance ??= GD.Load<CatAppearance>("res://assetsimagecat/white.tres");
        SetCatAppearance(0, Appearance);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) {
            var mouse = GetLocalMousePosition();
            if (mouse.DistanceTo(Snapshot(0)["center"].AsVector2()) < 65) BeginDragCat(0, mouse);
            return;
        }
        base._UnhandledInput(input);
    }
    public void Reset(Vector2 origin) { ResetCat(0,origin); SelectCat(0); }
    public void SetWaterMode(bool enabled) { SetCatWater(0,enabled); RenderNow(); }
    public bool IsWater => Snapshot(0)["water"].AsBool();
}
