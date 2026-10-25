class_name InventoryItem
extends Resource
## Reusable item definition. Use a stable id to identify stackable items.
@export var id: StringName
@export var display_name := "物品"
@export_multiline var description := ""
@export var icon: Texture2D
@export_range(1, 999) var max_stack := 99
