extends Node2D
## Three collision fixtures share the exact polygons used by the fluid solver.

const BG = Color("192936")
const PANEL = Color("223846")
const EDGE = Color("54717c")
const MINT = Color("a3dbc1")
const WHITE = Color("f6edda")
const MUTED = Color("91a9af")
@onready var cat = $LiquidCat
var layout := 0
var press_enabled := false
var paused := false
var time := 0.0
var press_time := 0.0
var terrain: Array[PackedVector2Array] = []
var font: Font
var buttons: Array[Rect2] = [Rect2(292, 14, 54, 19), Rect2(351, 14, 54, 19), Rect2(410, 14, 54, 19)]

func _ready() -> void:
	var window := get_window()
	window.content_scale_size = Vector2i(480, 300)
	window.content_scale_mode = Window.CONTENT_SCALE_MODE_VIEWPORT
	window.content_scale_stretch = Window.CONTENT_SCALE_STRETCH_INTEGER
	window.size = Vector2i(1152, 720)
	RenderingServer.set_default_clear_color(BG)
	font = ThemeDB.fallback_font
	set_layout(0)

func rect_polygon(rect: Rect2) -> PackedVector2Array:
	return PackedVector2Array([rect.position, rect.position + Vector2(rect.size.x, 0), rect.end, rect.position + Vector2(0, rect.size.y)])

func set_layout(index: int) -> void:
	layout = index
	press_enabled = false
	time = 0
	press_time = 0
	cat.reset(Vector2(95, 130) if layout != 2 else Vector2(232, 112))
	_update_terrain()

func _update_terrain() -> void:
	terrain.clear()
	terrain.append(rect_polygon(Rect2(0, 258, 480, 42)))
	terrain.append(rect_polygon(Rect2(0, 49, 10, 220)))
	terrain.append(rect_polygon(Rect2(470, 49, 10, 220)))
	match layout:
		0:
			terrain.append(PackedVector2Array([Vector2(20, 210), Vector2(160, 210), Vector2(221, 253), Vector2(20, 258)]))
			terrain.append(rect_polygon(Rect2(326, 220, 30, 38)))
			terrain.append(rect_polygon(Rect2(356, 195, 30, 63)))
			terrain.append(rect_polygon(Rect2(386, 170, 76, 88)))
		1:
			terrain.append(rect_polygon(Rect2(20, 211, 185, 47)))
			terrain.append(rect_polygon(Rect2(172, 49, 33, 141)))
			terrain.append(rect_polygon(Rect2(288, 212, 15, 46)))
			terrain.append(rect_polygon(Rect2(430, 212, 15, 46)))
		2:
			terrain.append(rect_polygon(Rect2(138, 173, 16, 85)))
			terrain.append(rect_polygon(Rect2(326, 173, 16, 85)))
			terrain.append(rect_polygon(Rect2(154, 244, 172, 14)))
	if press_enabled:
		var y := 102.0 + (1.0 - cos(press_time * 1.1)) * 0.5 * 134.0
		terrain.append(rect_polygon(Rect2(162, y - 12, 156, 12)))
	cat.obstacles = terrain

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		match event.keycode:
			KEY_1: set_layout(0)
			KEY_2: set_layout(1)
			KEY_3: set_layout(2)
			KEY_R: set_layout(layout)
			KEY_P:
				press_enabled = not press_enabled
				press_time = 0.0
			KEY_TAB:
				paused = not paused
				cat.simulation_enabled = not paused
	cat.gather = Input.is_physical_key_pressed(KEY_E)
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		for i in buttons.size():
			if buttons[i].has_point(get_global_mouse_position()):
				set_layout(i)

func _physics_process(delta: float) -> void:
	if not paused:
		time += delta
		if press_enabled:
			press_time += delta
	_update_terrain()
	queue_redraw()

func text(at: Vector2, content: String, color := WHITE, size := 10) -> void:
	draw_string(font, at, content, HORIZONTAL_ALIGNMENT_LEFT, -1, size, color)

func _draw() -> void:
	draw_rect(Rect2(0, 0, 480, 300), BG)
	for x in range(16, 480, 16):
		for y in range(60, 258, 16):
			draw_rect(Rect2(x, y, 1, 1), Color("2a414b"))
	draw_rect(Rect2(0, 0, 480, 47), PANEL)
	draw_line(Vector2(16, 46), Vector2(464, 46), EDGE)
	draw_rect(Rect2(16, 15, 3, 17), MINT)
	text(Vector2(26, 23), "LIQUID / CAT", WHITE, 16)
	text(Vector2(27, 36), "01   WHITE CAT     /     COHESIVE FLUID", MUTED, 7)
	var names := ["1  SLOPE", "2  GAP", "3  BOWL"]
	for i in buttons.size():
		draw_rect(buttons[i], MINT if layout == i else Color("304b58"))
		text(buttons[i].position + Vector2(5, 13), names[i], BG if layout == i else WHITE, 8)
	for i in terrain.size():
		var polygon := terrain[i]
		draw_colored_polygon(polygon, Color("3a5360"))
		var outline := polygon.duplicate()
		outline.append(polygon[0])
		draw_polyline(outline, EDGE, 1)
		if i > 2:
			draw_line(polygon[0], polygon[1], MINT, 2)
	if press_enabled:
		text(Vector2(185, 72), "PRESS / ACTIVE", Color("e9a6b2"), 9)
	var descriptions := ["SLOPE + STEPS", "21 PX CHANNEL + CATCH BASIN", "CONTAINER + MOVING PRESS [P]"]
	text(Vector2(18, 64), descriptions[layout], MUTED, 8)
	draw_rect(Rect2(0, 268, 480, 32), PANEL)
	draw_line(Vector2(16, 268), Vector2(464, 268), EDGE)
	text(Vector2(16, 281), "ARROWS  move    SPACE  hop    MOUSE  drag    E  gather", WHITE, 9)
	text(Vector2(16, 294), "R  reset    P  press    D  particles    TAB  pause", MUTED, 8)
	text(Vector2(361, 294), "%02d particles / %s" % [cat.points.size(), "PAUSED" if paused else "LIVE"], MINT, 8)
