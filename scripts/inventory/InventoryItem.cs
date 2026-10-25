using Godot;

[GlobalClass]
public partial class InventoryItem : Resource
{
    [Export] public StringName Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "物品";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Texture2D? Icon { get; set; }
    [Export(PropertyHint.Range, "1,999,1")] public int MaxStack { get; set; } = 99;
}
