using Godot;

public partial class InventorySlot : Button
{
    public Inventory OwnerInventory { get; set; } = null!;
    public int SlotIndex { get; set; }
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (OwnerInventory.Slots[SlotIndex].Item == null) return default;
        SetDragPreview(new Label { Text = Text });
        return new Godot.Collections.Dictionary { ["inventory"] = OwnerInventory, ["slot"] = SlotIndex };
    }
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var value = data.AsGodotDictionary();
        return value.TryGetValue("inventory", out var owner) && owner.AsGodotObject() == OwnerInventory
            && value.TryGetValue("slot", out var index) && index.VariantType == Variant.Type.Int
            && index.AsInt32() >= 0 && index.AsInt32() < Inventory.Capacity;
    }
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (_CanDropData(atPosition, data)) OwnerInventory.MoveItem(data.AsGodotDictionary()["slot"].AsInt32(), SlotIndex);
    }
}
