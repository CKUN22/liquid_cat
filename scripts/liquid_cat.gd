extends Node2D
## Equal-mass cohesive fluid. Internal pair corrections are equal and opposite.
## Positions stay continuous; only the final density surface is pixelated.

const H = 13.0
const SPACING = 5.4
const RADIUS = 2.3
const SUBSTEPS = 3
const ITERATIONS = 2
const WIDTH = 480
const HEIGHT = 300
const INK = Color("414458")

@export var gravity := 260.0
@export var viscosity := 5.0
@export var cohesion := 24.0
@export var appearance: Resource = preload("res://assetsimagecat/white.tres")
@export var water_mode := false
var points := PackedVector2Array()
var velocities := PackedVector2Array()
var obstacles: Array[PackedVector2Array] = []
var densities := PackedFloat32Array()
var field := PackedFloat32Array()
var material_x := PackedFloat32Array()
var material_y := PackedFloat32Array()
var material_coordinates := PackedVector2Array()
var body_mask := PackedByteArray()
var feature_mask := PackedByteArray()
var body_sum := PackedInt32Array()
var face_size := Vector2i(21, 13)
var face_scale := 1.0
var face_supported := false
var _face_texture: Texture2D
var _face_image: Image
var _body_bounds := Rect2i()
var _last_feature_center := Vector2.ZERO
var image: Image
var surface: ImageTexture
var center := Vector2.ZERO
var face_position := Vector2.ZERO
var face_anchor := Vector2.ZERO
var tail := PackedVector2Array()
var elapsed := 0.0
var grounded := false
var dragging := false
var drag_offset := Vector2.ZERO
var jump_requested := false
var debug_particles := false
var simulation_enabled := true
var gather := false
var shape_drift := 0.0
var _pairs: Array[Vector3] = []
var swim_axis := 0.0

func _ready() -> void:
	_configure_controls()
	field.resize(WIDTH * HEIGHT)
	material_x.resize(WIDTH * HEIGHT)
	material_y.resize(WIDTH * HEIGHT)
	body_mask.resize(WIDTH * HEIGHT)
	feature_mask.resize(WIDTH * HEIGHT)
	body_sum.resize((WIDTH + 1) * (HEIGHT + 1))
	image = Image.create(WIDTH, HEIGHT, false, Image.FORMAT_RGBA8)
	surface = ImageTexture.create_from_image(image)
	reset(Vector2(96, 125))

func reset(origin: Vector2) -> void:
	points.clear()
	velocities.clear()
	material_coordinates.clear()
	for row in range(-5, 6):
		for col in range(-5, 6):
			var offset := Vector2((col + 0.5 * (row & 1)) * SPACING, row * SPACING * 0.87)
			if offset.length() < 27.0:
				points.append(origin + offset)
				velocities.append(Vector2.ZERO)
				material_coordinates.append(offset)
	densities.resize(points.size())
	center = _centroid()
	face_position = center - Vector2(0, 2)
	_last_feature_center = center
	face_anchor = face_position
	tail.clear()
	for i in range(9):
		tail.append(center + Vector2(21 + i * 1.7, 7 - i * 1.5))
	dragging = false
	shape_drift = 0.0
	_render_surface()

func _centroid() -> Vector2:
	var result := Vector2.ZERO
	for p in points:
		result += p
	return result / maxf(points.size(), 1)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed and get_global_mouse_position().distance_to(center) < 65:
			dragging = true
			drag_offset = center - get_global_mouse_position()
		elif not event.pressed:
			dragging = false
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode == KEY_SPACE or event.physical_keycode == KEY_W or event.physical_keycode == KEY_UP:
			jump_requested = true
		if event.physical_keycode == KEY_V:
			debug_particles = not debug_particles
		if event.physical_keycode == KEY_F:
			set_water_mode(not water_mode)

func set_water_mode(enabled: bool) -> void:
	# Switching states changes constitutive behavior, never positions or mass.
	water_mode = enabled
	_render_surface()
	queue_redraw()

func _configure_controls() -> void:
	var bindings := {"cat_left": [KEY_A, KEY_LEFT], "cat_right": [KEY_D, KEY_RIGHT], "cat_up": [KEY_W, KEY_UP], "cat_down": [KEY_S, KEY_DOWN]}
	for action in bindings:
		if not InputMap.has_action(action):
			InputMap.add_action(action)
		for code in bindings[action]:
			var event := InputEventKey.new()
			event.physical_keycode = code
			if not InputMap.action_has_event(action, event):
				InputMap.action_add_event(action, event)

func movement_axes() -> Vector2:
	return Vector2(Input.get_axis("cat_left", "cat_right"), Input.get_axis("cat_up", "cat_down"))

func _physics_process(delta: float) -> void:
	elapsed += delta
	if simulation_enabled:
		var step := minf(delta, 1.0 / 30.0) / SUBSTEPS
		var axes := movement_axes()
		swim_axis = axes.y
		gather = Input.is_physical_key_pressed(KEY_E)
		if jump_requested and water_mode:
			splash()
		elif jump_requested and grounded:
			for i in points.size():
				velocities[i].y -= 130.0
		jump_requested = false
		grounded = false
		for substep in SUBSTEPS:
			_step(step, axes.x)
	center = _centroid()
	_update_features(delta)
	_render_surface()
	queue_redraw()

func _step(dt: float, movement: float) -> void:
	var before := points.duplicate()
	var com := _centroid()
	var attraction := PackedVector2Array()
	attraction.resize(points.size())
	var mean_force := Vector2.ZERO
	# Gentle long-range cohesion discourages permanent separated droplets.
	# Remove its mean so this aesthetic constraint cannot move the center of mass.
	for i in points.size():
		var offset := com - points[i]
		var strength := (0.0 if water_mode else cohesion)
		if gather:
			strength = cohesion * 2.5
		attraction[i] = offset * strength
		# S crouches by flattening about the mass center; this is internal force.
		if not water_mode and swim_axis > 0:
			attraction[i] += Vector2(-offset.x * 12, offset.y * 55)
		mean_force += attraction[i]
	mean_force /= points.size()
	for i in points.size():
		var acceleration := Vector2(movement * 210.0, gravity) + attraction[i] - mean_force
		if water_mode:
			acceleration.y += swim_axis * 420.0
		if dragging:
			acceleration += ((get_global_mouse_position() + drag_offset - com) * 38.0 - velocities[i] * 7.0).limit_length(2400)
		velocities[i] = (velocities[i] + acceleration * dt).limit_length(240)
		points[i] += velocities[i] * dt
	for iteration in ITERATIONS:
		_build_neighbors()
		var internal_center := _centroid()
		for pair in _pairs:
			var i := int(pair.x)
			var j := int(pair.y)
			var offset := points[j] - points[i]
			var distance := offset.length()
			if distance < 0.001:
				continue
			var q := maxf(0, 1.0 - distance / H)
			var pressure := (densities[i] + densities[j] - 5.8) * 0.22
			var near_pressure := q * q * 0.35
			var separation := maxf(0, SPACING * 0.88 - distance) * 0.36
			# Water supports compression, but no tensile pressure pulling it into a ball.
			var correction := offset / distance * clampf(pressure * q + near_pressure + separation, 0.0 if water_mode else -0.18, 0.65)
			points[i] -= correction
			points[j] += correction
		shape_drift = maxf(shape_drift, internal_center.distance_to(_centroid()))
		for i in points.size():
			points[i] = _collide(points[i])
	for i in points.size():
		velocities[i] = (points[i] - before[i]) / dt
		velocities[i] *= exp(-0.35 * dt)
	# Pairwise viscosity preserves linear momentum, unlike per-particle smoothing.
	for pair in _pairs:
		var i := int(pair.x)
		var j := int(pair.y)
		var impulse := (velocities[j] - velocities[i]) * minf(0.16, (0.65 if water_mode else viscosity) * dt * pair.z)
		velocities[i] += impulse
		velocities[j] -= impulse

func _build_neighbors() -> void:
	_pairs.clear()
	densities.fill(0.0)
	for i in points.size():
		for j in range(i + 1, points.size()):
			var distance_squared := points[i].distance_squared_to(points[j])
			if distance_squared < H * H:
				var q := 1.0 - sqrt(distance_squared) / H
				densities[i] += q * q
				densities[j] += q * q
				_pairs.append(Vector3(i, j, q))

func _collide(p: Vector2) -> Vector2:
	for polygon in obstacles:
		var inside := Geometry2D.is_point_in_polygon(p, polygon)
		var nearest := Vector2.ZERO
		var normal := Vector2.UP
		var best := INF
		for edge in polygon.size():
			var a := polygon[edge]
			var b := polygon[(edge + 1) % polygon.size()]
			var candidate := Geometry2D.get_closest_point_to_segment(p, a, b)
			var d := p.distance_squared_to(candidate)
			if d < best:
				best = d
				nearest = candidate
				normal = Vector2((b - a).y, -(b - a).x).normalized()
		if inside:
			p = nearest + normal * RADIUS
			if normal.y < -0.4:
				grounded = true
		elif best < RADIUS * RADIUS:
			var outward := (p - nearest).normalized()
			p = nearest + outward * RADIUS
			if outward.y < -0.4:
				grounded = true
	p.x = clampf(p.x, 12, WIDTH - 12)
	p.y = clampf(p.y, 82, HEIGHT - 35)
	return p

func splash() -> void:
	# Local external impulse creates a crest, which relaxes under gravity/pressure.
	for i in points.size():
		var dx := points[i].x - center.x
		velocities[i] += Vector2(dx * 1.5, -115.0 * exp(-dx * dx / 150.0))

func _fur_color() -> Color:
	return appearance.fur.lerp(Color("74d4e8"), 0.72) if water_mode else appearance.fur

func _shade_color() -> Color:
	return Color("438da9") if water_mode else appearance.shade

func _update_features(delta: float) -> void:
	# Face placement uses the current rendered body mask, below. The physical
	# centroid can be in a wall or between separate pools and is not a face anchor.
	var rightmost := points[0]
	for p in points:
		if p.x > rightmost.x:
			rightmost = p
	tail[0] = rightmost
	for i in range(1, tail.size()):
		var tail_target := tail[i - 1] + Vector2(1.8, -1.7 + sin(elapsed * 3 - i * 0.3) * 0.3)
		tail[i] = _collide(tail[i].lerp(tail_target, 1.0 - exp(-18 * delta)))

func _render_surface() -> void:
	field.fill(0)
	material_x.fill(0)
	material_y.fill(0)
	body_mask.fill(0)
	image.fill(Color.TRANSPARENT)
	var lower := Vector2i(WIDTH - 1, HEIGHT - 1)
	var upper := Vector2i.ZERO
	for particle in points.size():
		var p := points[particle]
		var origin := Vector2i(p)
		lower = lower.min(origin - Vector2i(10, 10))
		upper = upper.max(origin + Vector2i(10, 10))
		for y in range(maxi(0, origin.y - 9), mini(HEIGHT, origin.y + 10)):
			for x in range(maxi(0, origin.x - 9), mini(WIDTH, origin.x + 10)):
				var radius_squared := Vector2(x, y).distance_squared_to(p) / 81.0
				if radius_squared < 1:
					var contribution := (1 - radius_squared) * (1 - radius_squared)
					var cell := y * WIDTH + x
					field[cell] += contribution
					material_x[cell] += contribution * material_coordinates[particle].x
					material_y[cell] += contribution * material_coordinates[particle].y
	lower = lower.clamp(Vector2i(1, 1), Vector2i(WIDTH - 2, HEIGHT - 2))
	upper = upper.clamp(Vector2i(1, 1), Vector2i(WIDTH - 2, HEIGHT - 2))
	# First build the real visible body, including holes and solid occlusion.
	for y in range(lower.y, upper.y + 1):
		for x in range(lower.x, upper.x + 1):
			var index := y * WIDTH + x
			if field[index] < 0.58:
				continue
			var in_solid := false
			for polygon in obstacles:
				if Geometry2D.is_point_in_polygon(Vector2(x, y), polygon):
					in_solid = true
					break
			if in_solid:
				continue
			body_mask[index] = 1
	_select_face_anchor(lower, upper)
	var base_color := _fur_color()
	var shadow_color := _shade_color()
	var coat_center := (Vector2(lower) + Vector2(upper)) * 0.5
	var coat_extent := Vector2(upper - lower).max(Vector2(30, 25))
	for y in range(lower.y, upper.y + 1):
		for x in range(lower.x, upper.x + 1):
			var index := y * WIDTH + x
			if body_mask[index] == 0:
				continue
			var color := base_color
			if appearance.striped:
				# Fur always reconstructs its recognizable coat in body space.
				# Fluid particle mixing must not permanently scramble its identity.
				var u := (x - coat_center.x) * 54.0 / coat_extent.x
				var v := (y - coat_center.y) * 48.0 / coat_extent.y
				if water_mode:
					u = material_x[index] / field[index]
					v = material_y[index] / field[index]
				if absf(u) > 9 and posmod(int(v + sin(u * 0.19) * 3), 11) < 3:
					var face_distance := Vector2((x - face_anchor.x) / maxf(face_size.x * 0.65, 1), (y - face_anchor.y) / maxf(face_size.y * 0.8, 1)).length()
					var marking := smoothstep(0.85, 1.35, face_distance) if face_supported else 1.0
					color = color.lerp(appearance.stripe, marking * (0.35 if water_mode else 1.0))
			if field[index - 1] < 0.58 or field[index + 1] < 0.58 or field[index - WIDTH] < 0.58 or field[index + WIDTH] < 0.58:
				color = INK
			elif field[mini(field.size() - 1, index + WIDTH * 2)] < 0.58:
				color = shadow_color
			elif water_mode and field[maxi(0, index - WIDTH * 3)] < 0.58:
				color = Color("d8fbf4")
			elif water_mode and posmod(x + int(elapsed * 10), 29) < 5 and posmod(y + int(sin(elapsed * 2) * 2), 11) == 0:
				color = Color("b8f4ef")
			image.set_pixel(x, y, color)
	_paint_features()
	surface.update(image)

func _face_rect(anchor: Vector2i, size: Vector2i, padding: int) -> Rect2i:
	return Rect2i(anchor - Vector2i(size.x / 2, size.y / 2) - Vector2i.ONE * padding, size + Vector2i.ONE * padding * 2)

func _covered(rect: Rect2i) -> bool:
	if rect.position.x < 1 or rect.position.y < 1 or rect.end.x >= WIDTH or rect.end.y >= HEIGHT:
		return false
	if not _body_bounds.encloses(rect):
		return false
	var stride := WIDTH + 1
	var filled := body_sum[rect.end.y * stride + rect.end.x] - body_sum[rect.position.y * stride + rect.end.x] - body_sum[rect.end.y * stride + rect.position.x] + body_sum[rect.position.y * stride + rect.position.x]
	return filled == rect.size.x * rect.size.y

func _select_face_anchor(lower: Vector2i, upper: Vector2i) -> void:
	# Summed area queries require the entire face AND its fur margin to exist.
	var tracking_position := face_position + center - _last_feature_center
	_last_feature_center = center
	body_sum.fill(0)
	_body_bounds = Rect2i(lower, upper - lower + Vector2i.ONE)
	for y in range(lower.y, upper.y + 1):
		var row := 0
		for x in range(lower.x, upper.x + 1):
			row += body_mask[y * WIDTH + x]
			body_sum[(y + 1) * (WIDTH + 1) + x + 1] = body_sum[y * (WIDTH + 1) + x + 1] + row
	var remaining := body_mask.duplicate()
	var largest := PackedInt32Array()
	for y in range(lower.y, upper.y + 1):
		for x in range(lower.x, upper.x + 1):
			var seed := y * WIDTH + x
			if remaining[seed] == 0:
				continue
			var component := PackedInt32Array([seed])
			remaining[seed] = 0
			var head := 0
			while head < component.size():
				var cell := component[head]
				head += 1
				for neighbor in [cell - 1, cell + 1, cell - WIDTH, cell + WIDTH]:
					if remaining[neighbor] != 0:
						remaining[neighbor] = 0
						component.append(neighbor)
			if component.size() > largest.size():
				largest = component
	feature_mask.fill(0)
	for cell in largest:
		feature_mask[cell] = 1
	face_supported = false
	# A shallow puddle needs a smaller face, not a detached rectangular head.
	for scale_value in [1.0, 0.85, 0.7, 0.55, 0.4, 0.25, 0.16]:
		var size := Vector2i(maxi(3, roundi(21 * scale_value)), maxi(2, roundi(13 * scale_value)))
		var padding := 2 if scale_value >= 0.4 else 1
		var best_score := INF
		var best := Vector2i.ZERO
		for cell in largest:
			var candidate := Vector2i(cell % WIDTH, cell / WIDTH)
			if not _covered(_face_rect(candidate, size, padding)):
				continue
			var score := Vector2(candidate).distance_squared_to(center - Vector2(0, 3)) * 0.3 + Vector2(candidate).distance_squared_to(tracking_position) * 0.7
			if score < best_score:
				best_score = score
				best = candidate
		if best_score < INF:
			face_anchor = Vector2(best)
			face_position = face_anchor
			face_size = size
			face_scale = scale_value
			face_supported = true
			return

func _solid_pixel(pixel: Vector2i) -> bool:
	for polygon in obstacles:
		if Geometry2D.is_point_in_polygon(Vector2(pixel), polygon):
			return true
	return false

func _paint_ear(x: int) -> void:
	var top := int(face_anchor.y)
	while top > 1 and feature_mask[top * WIDTH + x] != 0:
		top -= 1
	var scale_value := maxf(face_scale, 0.4)
	var base := Vector2(x, top + 3 * scale_value)
	var tip := base + Vector2(-1, -12) * scale_value
	var triangle := PackedVector2Array([base + Vector2(-6, 2) * scale_value, tip, base + Vector2(6, 2) * scale_value])
	for y in range(maxi(1, floori(tip.y)), mini(HEIGHT - 1, ceili(base.y + 3))):
		for px in range(maxi(1, floori(base.x - 6 * scale_value)), mini(WIDTH - 1, ceili(base.x + 6 * scale_value + 1))):
			var pixel := Vector2i(px, y)
			if _solid_pixel(pixel) or not Geometry2D.is_point_in_polygon(Vector2(pixel), triangle):
				continue
			var color := _fur_color()
			var edge := minf(Geometry2D.get_closest_point_to_segment(Vector2(pixel), triangle[0], tip).distance_to(Vector2(pixel)), Geometry2D.get_closest_point_to_segment(Vector2(pixel), tip, triangle[2]).distance_to(Vector2(pixel)))
			if edge < 0.8:
				color = INK
			elif y > tip.y + 4 * scale_value and y < base.y and absf(px - x) < 2 * scale_value:
				color = Color("b8f4ef") if water_mode else appearance.ear
			image.set_pixel(px, y, color)

func _paint_features() -> void:
	if not face_supported:
		return
	_paint_ear(roundi(face_anchor.x - face_size.x * 0.45))
	_paint_ear(roundi(face_anchor.x + face_size.x * 0.45))
	if _face_texture != appearance.face:
		_face_texture = appearance.face
		_face_image = _face_texture.get_image()
		if _face_image.is_compressed():
			_face_image.decompress()
	var origin := Vector2i(face_anchor) - Vector2i(face_size.x / 2, face_size.y / 2)
	for y in face_size.y:
		for x in face_size.x:
			var source := Vector2i(mini(20, int((x + 0.5) * 21 / face_size.x)), mini(12, int((y + 0.5) * 13 / face_size.y)))
			var color := _face_image.get_pixelv(source)
			if fmod(elapsed, 4.4) > 4.24 and source.y < 7:
				color.a = 0
				if source.y == 4 and (source.x >= 3 and source.x <= 5 or source.x >= 15 and source.x <= 17):
					color = INK
			if color.a > 0:
				var pixel := origin + Vector2i(x, y)
				image.set_pixelv(pixel, image.get_pixelv(pixel).lerp(color, color.a))
	# Forehead markings are clipped to real fur, just like the face pixels.
	if appearance.striped:
		for mark in range(-1, 2):
			for step in range(3):
				var pixel := Vector2i(face_anchor) + Vector2i(roundi(mark * (4 - step * 0.5) * face_scale), -face_size.y / 2 - 2 + step)
				if pixel.x > 0 and pixel.x < WIDTH and pixel.y > 0 and pixel.y < HEIGHT and body_mask[pixel.y * WIDTH + pixel.x] != 0:
					image.set_pixelv(pixel, appearance.stripe)

func _draw() -> void:
	if surface == null:
		return
	for i in range(tail.size() - 1):
		draw_line(tail[i].round(), tail[i + 1].round(), INK, 6)
	for i in range(tail.size() - 1):
		var tail_color := _fur_color()
		if appearance.striped and i % 3 == 0:
			tail_color = appearance.stripe.lerp(tail_color, 0.65 if water_mode else 0)
		draw_line(tail[i].round(), tail[i + 1].round(), tail_color, 4)
	draw_texture(surface, Vector2.ZERO)
	if debug_particles:
		for p in points:
			draw_circle(p, 1, Color("de99a9"))
		draw_line(center - Vector2(4, 0), center + Vector2(4, 0), Color("56d9bd"))
		draw_line(center - Vector2(0, 4), center + Vector2(0, 4), Color("56d9bd"))

func diagnostics() -> Dictionary:
	var max_speed := 0.0
	var finite := true
	var penetrating := 0
	for i in points.size():
		finite = finite and points[i].is_finite() and velocities[i].is_finite()
		max_speed = maxf(max_speed, velocities[i].length())
		for polygon in obstacles:
			if Geometry2D.is_point_in_polygon(points[i], polygon):
				penetrating += 1
	return {"particles": points.size(), "center": center, "finite": finite, "max_speed": max_speed, "penetrating": penetrating, "internal_center_drift": shape_drift, "water": water_mode, "coat": appearance.display_name}
