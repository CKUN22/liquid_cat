using Godot;

[GlobalClass]
public partial class CatAppearance : Resource
{
    [Export] public string DisplayName { get; set; } = "WHITE";
    [Export] public Color Fur { get; set; } = new("fffdf2");
    [Export] public Color Shade { get; set; } = new("cbd5df");
    [Export] public Color Ear { get; set; } = new("e9a6b2");
    [Export] public Texture2D Face { get; set; } = GD.Load<Texture2D>("res://assetsimagecat/face.svg");
}
