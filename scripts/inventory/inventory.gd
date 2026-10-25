extends CanvasLayer
## Global inventory shared across game scenes. Opening the modal pauses gameplay.
signal changed
const ItemDefinition = preload("res://scripts/inventory/inventory_item.gd")
const SlotButton = preload("res://scripts/inventory/inventory_slot.gd")
const CAPACITY := 24
const UI_SCALE := 2.0
var slots: Array[Dictionary] = []
var is_open := false
var selected_index := 0
var _was_paused := false
var _overlay: Control
var _buttons: Array[Button] = []
var _details: Label
var _capacity: Label
var _panel: PanelContainer

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	layer = 100
	for i in CAPACITY:
		slots.append({"item": null, "count": 0})
	if not InputMap.has_action("inventory_toggle"):
		InputMap.add_action("inventory_toggle")
		var key := InputEventKey.new()
		key.physical_keycode = KEY_B
		InputMap.action_add_event("inventory_toggle", key)
	_build_ui()
	changed.connect(_refresh)
	_refresh()

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.echo:
		return
	if event.is_action_pressed("inventory_toggle"):
		set_open(not is_open)
		get_viewport().set_input_as_handled()
	elif is_open and event.is_action_pressed("ui_cancel"):
		set_open(false)
		get_viewport().set_input_as_handled()

func set_open(value: bool) -> void:
	if value == is_open:
		return
	is_open = value
	_overlay.visible = value
	if value:
		_was_paused = get_tree().paused
		get_tree().paused = true
		_refresh()
		_layout_panel.call_deferred()
	else:
		get_tree().paused = _was_paused
		get_viewport().gui_release_focus()

## Returns the number accepted; a full inventory leaves the remainder to the caller.
func add_item(item: Resource, amount: int = 1) -> int:
	if not item is ItemDefinition or amount <= 0 or item.id == &"":
		return 0
	var remaining := amount
	for slot in slots:
		if slot.item != null and slot.item.id == item.id:
			var accepted := mini(remaining, maxi(0, slot.item.max_stack - slot.count))
			slot.count += accepted
			remaining -= accepted
	for slot in slots:
		if remaining == 0:
			break
		if slot.item == null:
			var accepted := mini(remaining, item.max_stack)
			slot.item = item
			slot.count = accepted
			remaining -= accepted
	if remaining != amount:
		changed.emit()
	return amount - remaining

func remove_item(index: int, amount: int = 1) -> int:
	if index < 0 or index >= CAPACITY or amount <= 0:
		return 0
	var removed := mini(amount, slots[index].count)
	slots[index].count -= removed
	if slots[index].count == 0:
		slots[index].item = null
	if removed > 0:
		changed.emit()
	return removed

func move_item(source: int, target: int) -> void:
	if source < 0 or target < 0 or source >= CAPACITY or target >= CAPACITY or source == target:
		return
	var origin := slots[source]
	var destination := slots[target]
	if origin.item == null:
		return
	if destination.item != null and destination.item.id == origin.item.id:
		var moved := mini(origin.count, maxi(0, destination.item.max_stack - destination.count))
		destination.count += moved
		origin.count -= moved
		if origin.count == 0:
			origin.item = null
	else:
		slots[source] = destination
		slots[target] = origin
	selected_index = target
	changed.emit()

func _build_ui() -> void:
	_overlay = Control.new()
	_overlay.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	_overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(_overlay)
	var backdrop := ColorRect.new()
	backdrop.color = Color(0.03, 0.06, 0.09, 0.78)
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_overlay.add_child(backdrop)
	var panel := PanelContainer.new()
	_panel = panel
	var style := StyleBoxFlat.new()
	style.bg_color = Color("223846")
	style.border_color = Color("a3dbc1")
	style.set_border_width_all(2)
	style.set_content_margin_all(12)
	panel.add_theme_stylebox_override("panel", style)
	_overlay.add_child(panel)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 6)
	panel.add_child(column)
	var header := HBoxContainer.new()
	column.add_child(header)
	var title := Label.new()
	title.text = "背包"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.add_theme_font_size_override("font_size", 16)
	header.add_child(title)
	_capacity = Label.new()
	_capacity.add_theme_font_size_override("font_size", 16)
	header.add_child(_capacity)
	var close := Button.new()
	close.text = "×"
	close.tooltip_text = "关闭背包（B / Esc）"
	close.pressed.connect(func(): set_open(false))
	header.add_child(close)
	var grid := GridContainer.new()
	grid.columns = 6
	column.add_child(grid)
	for i in CAPACITY:
		var button := SlotButton.new()
		button.inventory = self
		button.slot_index = i
		button.custom_minimum_size = Vector2(46, 34)
		button.expand_icon = true
		button.add_theme_constant_override("icon_max_width", 24)
		button.add_theme_font_size_override("font_size", 16)
		button.pressed.connect(func():
			selected_index = i
			_refresh())
		grid.add_child(button)
		_buttons.append(button)
	_details = Label.new()
	_details.custom_minimum_size = Vector2(296, 32)
	_details.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_details.add_theme_font_size_override("font_size", 16)
	column.add_child(_details)
	var hint := Label.new()
	hint.text = "拖动整理 / 同类堆叠    B 开关 · Esc 关闭"
	hint.add_theme_font_size_override("font_size", 16)
	column.add_child(hint)
	_overlay.hide()
	get_viewport().size_changed.connect(_layout_panel)
	panel.minimum_size_changed.connect(_layout_panel)
	_layout_panel.call_deferred()

func _refresh() -> void:
	var used := 0
	for i in CAPACITY:
		var slot := slots[i]
		var button := _buttons[i]
		button.icon = slot.item.icon if slot.item != null else null
		button.text = str(slot.count) if slot.item != null else ""
		button.tooltip_text = slot.item.display_name if slot.item != null else "空格"
		button.modulate = Color("a3dbc1") if i == selected_index else Color.WHITE
		if slot.item != null:
			used += 1
	_capacity.text = "%d / %d" % [used, CAPACITY]
	var selected := slots[selected_index]
	_details.text = "%s ×%d\n%s" % [selected.item.display_name, selected.count, selected.item.description] if selected.item != null else "背包空空的，收集的物品会放在这里。" if used == 0 else "空格 · 拖动物品到这里"

func _layout_panel() -> void:
	if not is_instance_valid(_panel):
		return
	var available := get_viewport().get_visible_rect().size
	var minimum := _panel.get_combined_minimum_size()
	_panel.size = minimum
	# Preserve access to all slots and the close button in small game viewports.
	var factor := minf(UI_SCALE, minf((available.x - 16.0) / minimum.x, (available.y - 16.0) / minimum.y))
	if factor >= 1.0:
		factor = floorf(factor)
	_panel.scale = Vector2.ONE * maxf(factor, 0.1)
	_panel.position = ((available - _panel.size * _panel.scale) * 0.5).floor()
