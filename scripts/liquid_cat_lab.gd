extends Node2D
## Seven collision fixtures share the exact polygons used by the fluid solver.

const BG = Color("192936")
const PANEL = Color("223846")
const EDGE = Color("54717c")
const MINT = Color("a3dbc1")
const WHITE = Color("f6edda")
const MUTED = Color("91a9af")
const COATS = [preload("res://assetsimagecat/white.tres"), preload("res://assetsimagecat/tabby.tres"), preload("res://assetsimagecat/orange.tres"), preload("res://assetsimagecat/blue.tres")]
const ROOM_NAMES = ["SLOPE", "GAP", "PRESS", "POOL", "FALLS", "FUNNEL", "VESSELS"]
const DESCRIPTIONS = ["SLOPE + STEPS", "21 PX CHANNEL + CATCH BASIN", "CONTAINER + MOVING PRESS [P]", "STILL WATER / SPACE MAKES A WAVE", "THREE CASCADES + COLLECTION POOL", "FUNNEL + 28 PX THROAT", "CONNECTED VESSELS / 23 PX BOTTOM PASSAGE"]
@export_range(0, 6) var initial_layout := 0
@export var initial_water := false
@onready var cat = $LiquidCat
var coat_index := 0
var layout := 0
var press_enabled := false
var paused := false
var time := 0.0
var press_time := 0.0
var terrain: Array[PackedVector2Array] = []
var font: Font
var buttons: Array[Rect2] = []
var coat_buttons: Array[Rect2] = []
var water_button := Rect2(281, 45, 94, 17)

func _ready() -> void:
	var window := get_window()
	window.content_scale_size = Vector2i(480, 300)
	window.content_scale_mode = Window.CONTENT_SCALE_MODE_VIEWPORT
	window.content_scale_stretch = Window.CONTENT_SCALE_STRETCH_INTEGER
	window.size = Vector2i(1152, 720)
	RenderingServer.set_default_clear_color(BG)
	font = preload("res://assets/fonts/unifont-16.0.04.ttf")
	for i in ROOM_NAMES.size():
		buttons.append(Rect2(228 + i * 34, 12, 31, 22))
	for i in COATS.size():
		coat_buttons.append(Rect2(16 + i * 65, 45, 60, 17))
	cat.set_water_mode(initial_water)
	set_layout(initial_layout)

func rect_polygon(rect: Rect2) -> PackedVector2Array:
	return PackedVector2Array([rect.position, rect.position + Vector2(rect.size.x, 0), rect.end, rect.position + Vector2(0, rect.size.y)])

func set_layout(index: int) -> void:
	layout = clampi(index, 0, 6)
	press_enabled = false
	time = 0
	press_time = 0
	var spawns := [Vector2(95, 140), Vector2(95, 140), Vector2(232, 120), Vector2(232, 135), Vector2(73, 112), Vector2(240, 118), Vector2(180, 156)]
	cat.reset(spawns[layout])
	_update_terrain()

func set_coat(index: int) -> void:
	coat_index = posmod(index, COATS.size())
	cat.appearance = COATS[coat_index]
	cat._render_surface()
	cat.queue_redraw()

func _update_terrain() -> void:
	terrain.clear()
	terrain.append(rect_polygon(Rect2(0, 258, 480, 42)))
	terrain.append(rect_polygon(Rect2(0, 78, 10, 190)))
	terrain.append(rect_polygon(Rect2(470, 78, 10, 190)))
	match layout:
		0:
			terrain.append(PackedVector2Array([Vector2(20, 210), Vector2(160, 210), Vector2(221, 253), Vector2(20, 258)]))
			terrain.append(rect_polygon(Rect2(326, 220, 30, 38)))
			terrain.append(rect_polygon(Rect2(356, 195, 30, 63)))
			terrain.append(rect_polygon(Rect2(386, 170, 76, 88)))
		1:
			terrain.append(rect_polygon(Rect2(20, 211, 185, 47)))
			terrain.append(rect_polygon(Rect2(172, 78, 33, 112)))
			terrain.append(rect_polygon(Rect2(288, 212, 15, 46)))
			terrain.append(rect_polygon(Rect2(430, 212, 15, 46)))
		2:
			terrain.append(rect_polygon(Rect2(138, 173, 16, 85)))
			terrain.append(rect_polygon(Rect2(326, 173, 16, 85)))
			terrain.append(rect_polygon(Rect2(154, 244, 172, 14)))
		3:
			terrain.append(rect_polygon(Rect2(138, 166, 14, 92)))
			terrain.append(rect_polygon(Rect2(328, 166, 14, 92)))
			terrain.append(rect_polygon(Rect2(152, 251, 176, 7)))
		4:
			terrain.append(PackedVector2Array([Vector2(20, 153), Vector2(120, 153), Vector2(160, 176), Vector2(20, 176)]))
			terrain.append(PackedVector2Array([Vector2(163, 195), Vector2(242, 195), Vector2(275, 216), Vector2(163, 216)]))
			terrain.append(rect_polygon(Rect2(300, 229, 14, 29)))
			terrain.append(rect_polygon(Rect2(444, 205, 14, 53)))
		5:
			terrain.append(PackedVector2Array([Vector2(70, 114), Vector2(226, 205), Vector2(226, 235), Vector2(70, 235)]))
			terrain.append(PackedVector2Array([Vector2(410, 114), Vector2(410, 235), Vector2(254, 235), Vector2(254, 205)]))
		6:
			terrain.append(rect_polygon(Rect2(118, 148, 14, 110)))
			terrain.append(rect_polygon(Rect2(348, 148, 14, 110)))
			terrain.append(rect_polygon(Rect2(231, 110, 18, 125)))
	if press_enabled:
		var y := 102.0 + (1.0 - cos(press_time * 1.1)) * 0.5 * 134.0
		terrain.append(rect_polygon(Rect2(162, y - 12, 156, 12)))
	cat.obstacles = terrain

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		match event.physical_keycode:
			KEY_1: set_layout(0)
			KEY_2: set_layout(1)
			KEY_3: set_layout(2)
			KEY_4: set_layout(3)
			KEY_5: set_layout(4)
			KEY_6: set_layout(5)
			KEY_7: set_layout(6)
			KEY_C: set_coat(coat_index + 1)
			KEY_R: set_layout(layout)
			KEY_P:
				press_enabled = not press_enabled
				press_time = 0.0
			KEY_TAB:
				paused = not paused
				cat.simulation_enabled = not paused
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		for i in buttons.size():
			if buttons[i].has_point(get_global_mouse_position()):
				set_layout(i)
		for i in coat_buttons.size():
			if coat_buttons[i].has_point(get_global_mouse_position()):
				set_coat(i)
		if water_button.has_point(get_global_mouse_position()):
			cat.set_water_mode(not cat.water_mode)

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
		for y in range(90, 258, 16):
			draw_rect(Rect2(x, y, 1, 1), Color("2a414b"))
	draw_rect(Rect2(0, 0, 480, 71), PANEL)
	draw_line(Vector2(16, 70), Vector2(464, 70), EDGE)
	draw_rect(Rect2(16, 15, 3, 17), MINT)
	text(Vector2(26, 23), "LIQUID / CAT", WHITE, 16)
	text(Vector2(27, 36), "%s CAT / %s" % [cat.appearance.display_name, "WATER" if cat.water_mode else "COHESIVE FLUID"], MUTED, 7)
	for i in buttons.size():
		draw_rect(buttons[i], MINT if layout == i else Color("304b58"))
		text(buttons[i].position + Vector2(3, 9), str(i + 1), BG if layout == i else WHITE, 7)
		text(buttons[i].position + Vector2(2, 18), ROOM_NAMES[i].left(5), BG if layout == i else WHITE, 6)
	for i in coat_buttons.size():
		draw_rect(coat_buttons[i], Color("47616c") if coat_index == i else Color("304550"))
		draw_rect(Rect2(coat_buttons[i].position + Vector2(4, 5), Vector2(6, 6)), COATS[i].fur)
		text(coat_buttons[i].position + Vector2(14, 12), COATS[i].display_name, WHITE, 8)
	draw_rect(water_button, Color("74d4e8") if cat.water_mode else Color("304550"))
	text(water_button.position + Vector2(6, 12), "F  WATER " + ("ON" if cat.water_mode else "OFF"), BG if cat.water_mode else WHITE, 8)
	text(Vector2(393, 57), "C  COAT", MUTED, 8)
	for i in terrain.size():
		var polygon := terrain[i]
		draw_colored_polygon(polygon, Color("3a5360"))
		var outline := polygon.duplicate()
		outline.append(polygon[0])
		draw_polyline(outline, EDGE, 1)
		if i > 2:
			draw_line(polygon[0], polygon[1], MINT, 2)
	if press_enabled:
		text(Vector2(365, 82), "PRESS / ACTIVE", Color("e9a6b2"), 8)
	text(Vector2(18, 82), DESCRIPTIONS[layout], MUTED, 8)
	if layout == 3 or layout == 6:
		text(Vector2(18, 105), "F: water state", Color("74d4e8"), 8)
		text(Vector2(18, 117), "SPACE: splash", MUTED, 8)
	draw_rect(Rect2(0, 268, 480, 32), PANEL)
	draw_line(Vector2(16, 268), Vector2(464, 268), EDGE)
	text(Vector2(16, 281), "AD move  W hop/swim  S crouch/dive  SPACE splash/hop", WHITE, 9)
	text(Vector2(16, 294), "1-7 room  R reset  P press  V particles  E gather  TAB pause", MUTED, 8)
	text(Vector2(393, 294), "%02d / %s" % [cat.points.size(), "PAUSE" if paused else "LIVE"], MINT, 8)
