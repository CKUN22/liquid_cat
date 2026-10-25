using Godot;
using System;
using System.Collections.Generic;

public partial class Inventory : CanvasLayer
{
    [Signal] public delegate void ChangedEventHandler();
    public const int Capacity = 24;
    const float UiScale = 2;
    public sealed class Stack { public InventoryItem? Item; public int Count; }
    public List<Stack> Slots { get; } = new();
    public bool IsOpen { get; private set; }
    public int SelectedIndex { get; private set; }
    bool _wasPaused;
    Control _overlay = null!;
    PanelContainer _panel = null!;
    Label _details = null!, _capacity = null!;
    readonly List<InventorySlot> _buttons = new();
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; Layer = 100;
        for (int i = 0; i < Capacity; i++) Slots.Add(new Stack());
        if (!InputMap.HasAction("inventory_toggle")) {
            InputMap.AddAction("inventory_toggle");
            InputMap.ActionAddEvent("inventory_toggle", new InputEventKey { PhysicalKeycode = Key.B });
        }
        BuildUi(); Changed += Refresh; Refresh();
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Echo: true }) return;
        if (input.IsActionPressed("inventory_toggle")) { SetOpen(!IsOpen); GetViewport().SetInputAsHandled(); }
        else if (IsOpen && input.IsActionPressed("ui_cancel")) { SetOpen(false); GetViewport().SetInputAsHandled(); }
    }
    public void SetOpen(bool value)
    {
        if (IsOpen == value) return;
        IsOpen = value; _overlay.Visible = value;
        if (value) {
            _wasPaused = GetTree().Paused; GetTree().Paused = true; Refresh();
            Callable.From(LayoutPanel).CallDeferred();
        } else { GetTree().Paused = _wasPaused; GetViewport().GuiReleaseFocus(); }
    }
    // Returns accepted quantity; callers retain items that did not fit.
    public int AddItem(InventoryItem? item, int amount = 1)
    {
        if (item == null || amount <= 0 || item.Id.IsEmpty) return 0;
        int remaining = amount;
        foreach (var slot in Slots) if (slot.Item != null && slot.Item.Id == item.Id) {
            int accepted = Math.Min(remaining, Math.Max(0, slot.Item.MaxStack - slot.Count));
            slot.Count += accepted; remaining -= accepted;
        }
        foreach (var slot in Slots) {
            if (remaining == 0) break;
            if (slot.Item != null) continue;
            int accepted = Math.Min(remaining, Math.Max(1, item.MaxStack));
            slot.Item = item; slot.Count = accepted; remaining -= accepted;
        }
        if (remaining != amount) EmitSignal(SignalName.Changed);
        return amount - remaining;
    }
    public int RemoveItem(int index, int amount = 1)
    {
        if (index < 0 || index >= Capacity || amount <= 0) return 0;
        var slot = Slots[index]; int removed = Math.Min(amount, slot.Count);
        slot.Count -= removed; if (slot.Count == 0) slot.Item = null;
        if (removed > 0) EmitSignal(SignalName.Changed);
        return removed;
    }
    public void MoveItem(int source, int target)
    {
        if (source < 0 || target < 0 || source >= Capacity || target >= Capacity || source == target) return;
        var origin = Slots[source]; var destination = Slots[target];
        if (origin.Item == null) return;
        if (destination.Item != null && destination.Item.Id == origin.Item.Id) {
            int moved = Math.Min(origin.Count, Math.Max(0, destination.Item.MaxStack - destination.Count));
            destination.Count += moved; origin.Count -= moved;
            if (origin.Count == 0) origin.Item = null;
        } else { Slots[source] = destination; Slots[target] = origin; }
        SelectedIndex = target; EmitSignal(SignalName.Changed);
    }
    static void FullRect(Control control) => control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    static void FontSize(Control control) => control.AddThemeFontSizeOverride("font_size", 16);
    void BuildUi()
    {
        _overlay = new Control { TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        FullRect(_overlay); AddChild(_overlay);
        var backdrop = new ColorRect { Color = new Color(0.03f, 0.06f, 0.09f, 0.78f) };
        FullRect(backdrop); _overlay.AddChild(backdrop);
        _panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color("223846"), BorderColor = new Color("a3dbc1") };
        style.SetBorderWidthAll(2); style.SetContentMarginAll(12);
        _panel.AddThemeStyleboxOverride("panel", style); _overlay.AddChild(_panel);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 6); _panel.AddChild(column);
        var header = new HBoxContainer(); column.AddChild(header);
        var title = new Label { Text = "背包", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        FontSize(title); header.AddChild(title);
        _capacity = new Label(); FontSize(_capacity); header.AddChild(_capacity);
        var close = new Button { Text = "×", TooltipText = "关闭背包（B / Esc）" };
        close.Pressed += () => SetOpen(false); header.AddChild(close);
        var grid = new GridContainer { Columns = 6 }; column.AddChild(grid);
        for (int i = 0; i < Capacity; i++) {
            int index = i;
            var button = new InventorySlot { OwnerInventory = this, SlotIndex = i, CustomMinimumSize = new Vector2(46,34), ExpandIcon = true };
            button.AddThemeConstantOverride("icon_max_width",24); FontSize(button);
            button.Pressed += () => { SelectedIndex = index; Refresh(); };
            grid.AddChild(button); _buttons.Add(button);
        }
        _details = new Label { CustomMinimumSize = new Vector2(296,32), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        FontSize(_details); column.AddChild(_details);
        var hint = new Label { Text = "拖动整理 / 同类堆叠    B 开关 · Esc 关闭" };
        FontSize(hint); column.AddChild(hint);
        _overlay.Hide(); GetViewport().SizeChanged += LayoutPanel; _panel.MinimumSizeChanged += LayoutPanel;
        Callable.From(LayoutPanel).CallDeferred();
    }
    void Refresh()
    {
        int used = 0;
        for (int i = 0; i < Capacity; i++) {
            var slot = Slots[i]; var button = _buttons[i];
            button.Icon = slot.Item?.Icon; button.Text = slot.Item != null ? slot.Count.ToString() : "";
            button.TooltipText = slot.Item?.DisplayName ?? "空格";
            button.Modulate = i == SelectedIndex ? new Color("a3dbc1") : Colors.White;
            if (slot.Item != null) used++;
        }
        _capacity.Text = $"{used} / {Capacity}";
        var selected = Slots[SelectedIndex];
        _details.Text = selected.Item != null ? $"{selected.Item.DisplayName} ×{selected.Count}\n{selected.Item.Description}"
            : used == 0 ? "背包空空的，收集的物品会放在这里。" : "空格 · 拖动物品到这里";
    }
    public void LayoutPanel()
    {
        if (!IsInstanceValid(_panel)) return;
        var available = GetViewport().GetVisibleRect().Size;
        var minimum = _panel.GetCombinedMinimumSize(); _panel.Size = minimum;
        float factor = Math.Min(UiScale, Math.Min((available.X - 16) / minimum.X, (available.Y - 16) / minimum.Y));
        if (factor >= 1) factor = MathF.Floor(factor);
        _panel.Scale = Vector2.One * Math.Max(0.1f, factor);
        _panel.Position = ((available - _panel.Size * _panel.Scale) * 0.5f).Floor();
    }
}
