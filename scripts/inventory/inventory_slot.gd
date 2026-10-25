extends Button

var inventory: Node
var slot_index := 0

func _get_drag_data(_at_position: Vector2) -> Variant:
	if inventory.slots[slot_index].item == null:
		return null
	var preview := Label.new()
	preview.text = text
	set_drag_preview(preview)
	return {"inventory": inventory, "slot": slot_index}

func _can_drop_data(_at_position: Vector2, data: Variant) -> bool:
	return data is Dictionary and data.get("inventory") == inventory and data.get("slot") is int

func _drop_data(_at_position: Vector2, data: Variant) -> void:
	inventory.move_item(data.slot, slot_index)
