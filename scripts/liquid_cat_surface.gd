extends RefCounted
## Cropped white-cat raster; shared solid coverage, independent of scene nodes.
const INK := Color("414458")
var WIDTH := 0
var HEIGHT := 0
var origin := Vector2.ZERO
var body_scale := 1.0
var world_size := Vector2i.ZERO
var solid := PackedByteArray()
var points := PackedVector2Array()
var center := Vector2.ZERO
var elapsed := 0.0
var water_mode := false
var appearance: Resource = preload("res://assetsimagecat/white.tres")
var field := PackedFloat32Array()
var body_mask := PackedByteArray()
var feature_mask := PackedByteArray()
var body_sum := PackedInt32Array()
var face_size := Vector2i(21, 13)
var face_scale := 1.0
var face_supported := false
var _face_growth_at := 0.0
var face_position := Vector2.ZERO
var face_anchor := Vector2.ZERO
var _last_feature_center := Vector2.ZERO
var _body_bounds := Rect2i()
var _face_texture: Texture2D
var _face_image: Image
var image: Image
var features_image: Image
var surface: ImageTexture
var features_surface: ImageTexture
static var _shared_face_image: Image

func _init() -> void:
	# Constructed on the main thread. Never read back GPU textures from a worker
	# while the main thread waits for that worker group to finish.
	_face_texture = appearance.face
	if _shared_face_image == null:
		_shared_face_image = _face_texture.get_image()
		if _shared_face_image.is_compressed(): _shared_face_image.decompress()
	_face_image = _shared_face_image

func reset_tracking(normalized_center: Vector2) -> void:
	face_position = normalized_center - origin - Vector2(0, 2)
	face_anchor = face_position
	_last_feature_center = normalized_center - origin
	face_supported = false
	_face_growth_at = 0.0

func _fur_color() -> Color:
	return appearance.fur.lerp(Color("74d4e8"), 0.72) if water_mode else appearance.fur
func _shade_color() -> Color:
	return Color("438da9") if water_mode else appearance.shade

func prepare(world_points: PackedVector2Array, world_center: Vector2, scale_value: float, water: bool, clock: float, size: Vector2i, solid_mask: PackedByteArray) -> void:
	body_scale = scale_value; water_mode = water; elapsed = clock
	world_size = size; solid = solid_mask
	var local_size := Vector2i(ceili(size.x / scale_value), ceili(size.y / scale_value))
	var radius := 12 if water else 9
	var lower := local_size - Vector2i.ONE
	var upper := Vector2i.ZERO
	for p in world_points:
		var pixel := Vector2i(p / scale_value)
		lower = lower.min(pixel - Vector2i.ONE * (radius + 1))
		upper = upper.max(pixel + Vector2i.ONE * (radius + 1))
	lower = lower.clamp(Vector2i.ONE, local_size - Vector2i.ONE * 2)
	upper = upper.clamp(Vector2i.ONE, local_size - Vector2i.ONE * 2)
	var new_origin := Vector2(maxi(0, lower.x - 2), maxi(0, lower.y - 18))
	var new_width := ceili((upper.x - new_origin.x + 3) / 16.0) * 16
	var new_height := ceili((upper.y - new_origin.y + 3) / 16.0) * 16
	face_position += origin - new_origin
	face_anchor += origin - new_origin
	_last_feature_center += origin - new_origin
	origin = new_origin
	center = world_center / scale_value - origin
	if WIDTH != new_width or HEIGHT != new_height:
		WIDTH = new_width; HEIGHT = new_height
		field.resize(WIDTH * HEIGHT); body_mask.resize(WIDTH * HEIGHT)
		feature_mask.resize(WIDTH * HEIGHT); body_sum.resize((WIDTH + 1) * (HEIGHT + 1))
		image = Image.create(WIDTH, HEIGHT, false, Image.FORMAT_RGBA8)
		features_image = Image.create(WIDTH, HEIGHT, false, Image.FORMAT_RGBA8)
	field.fill(0); body_mask.fill(0)
	points.clear()
	for world_p in world_points:
		var p := world_p / scale_value - origin
		points.append(p)
		var pixel := Vector2i(p)
		for y in range(maxi(1, pixel.y - radius), mini(HEIGHT - 1, pixel.y + radius + 1)):
			for x in range(maxi(1, pixel.x - radius), mini(WIDTH - 1, pixel.x + radius + 1)):
				var distance := Vector2(x, y).distance_squared_to(p) / float(radius * radius)
				if distance < 1: field[y * WIDTH + x] += (1 - distance) * (1 - distance)
	lower -= Vector2i(origin); upper -= Vector2i(origin)
	for y in range(lower.y, upper.y + 1):
		for x in range(lower.x, upper.x + 1):
			var cell := y * WIDTH + x
			if field[cell] >= 0.58 and not _solid_pixel(Vector2i(x, y)): body_mask[cell] = 1
	if water: _fill_water_holes(lower, upper)
	_select_face_anchor(lower, upper)

func mark_owner(owners: PackedInt32Array, size: Vector2i, id: int, scale_value: float) -> void:
	for y in range(1, HEIGHT - 1):
		for x in range(1, WIDTH - 1):
			if body_mask[y * WIDTH + x] == 0: continue
			var world := Vector2i((Vector2(x, y) + origin) * scale_value)
			if world.x >= 0 and world.y >= 0 and world.x < size.x and world.y < size.y: owners[world.y * size.x + world.x] = id

func paint(owners: PackedInt32Array, size: Vector2i, id: int, scale_value: float, merging: bool) -> void:
	image.fill(Color.TRANSPARENT)
	features_image.fill(Color.TRANSPARENT)
	var fur := _fur_color()
	var shade := _shade_color()
	for y in range(1, HEIGHT - 1):
		for x in range(1, WIDTH - 1):
			var cell := y * WIDTH + x
			if body_mask[cell] == 0: continue
			var color := fur
			if body_mask[cell - 1] == 0 or body_mask[cell + 1] == 0 or body_mask[cell - WIDTH] == 0 or body_mask[cell + WIDTH] == 0: color = INK
			elif body_mask[mini(body_mask.size() - 1, cell + WIDTH * 2)] == 0: color = shade
			elif water_mode and body_mask[maxi(0, cell - WIDTH * 3)] == 0: color = Color("d8fbf4")
			elif water_mode and posmod(x + int(origin.x) + int(elapsed * 10), 29) < 5 and posmod(y + int(origin.y) + int(sin(elapsed * 2) * 2), 11) == 0: color = Color("b8f4ef")
			if not merging and color != fur:
				var world := Vector2i((Vector2(x, y) + origin) * scale_value)
				if _other_body_near(owners, size, id, world): color = fur
			image.set_pixel(x, y, color)
	var body_image := image
	image = features_image
	_paint_features()
	image = body_image

func _other_body_near(owners: PackedInt32Array, size: Vector2i, id: int, world: Vector2i) -> bool:
	for dy in range(-2, 3):
		for dx in range(-2, 3):
			var p := world + Vector2i(dx, dy)
			if p.x < 0 or p.y < 0 or p.x >= size.x or p.y >= size.y: continue
			var owner := owners[p.y * size.x + p.x]
			if owner >= 0 and owner != id: return true
	return false

func upload() -> void:
	if surface == null: surface = ImageTexture.create_from_image(image)
	elif surface.get_width() != WIDTH or surface.get_height() != HEIGHT: surface.set_image(image)
	else: surface.update(image)
	if features_surface == null: features_surface = ImageTexture.create_from_image(features_image)
	elif features_surface.get_width() != WIDTH or features_surface.get_height() != HEIGHT: features_surface.set_image(features_image)
	else: features_surface.update(features_image)

func _fill_water_holes(lower: Vector2i, upper: Vector2i) -> void:
	var visited := body_mask.duplicate()
	for y in range(lower.y, upper.y + 1):
		for x in range(lower.x, upper.x + 1):
			var seed := y * WIDTH + x
			if visited[seed] != 0:
				continue
			var region := PackedInt32Array([seed])
			visited[seed] = 2
			var head := 0
			var exterior := false
			var solid := false
			while head < region.size():
				var cell := region[head]
				head += 1
				var pixel := Vector2i(cell % WIDTH, cell / WIDTH)
				if pixel.x <= lower.x or pixel.y <= lower.y or pixel.x >= upper.x or pixel.y >= upper.y:
					exterior = true
					continue
				solid = solid or _solid_pixel(pixel)
				for neighbor in [cell - 1, cell + 1, cell - WIDTH, cell + WIDTH]:
					if visited[neighbor] == 0:
						visited[neighbor] = 2
						region.append(neighbor)
			if not exterior and not solid and region.size() <= 96:
				for cell in region:
					body_mask[cell] = 1

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
	var tracking_cell := roundi(tracking_position.y) * WIDTH + roundi(tracking_position.x)
	var tracking_component := false
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
			var contains_tracking := component.has(tracking_cell)
			if contains_tracking or (not tracking_component and component.size() > largest.size()):
				largest = component
				tracking_component = contains_tracking
	feature_mask.fill(0)
	for cell in largest:
		feature_mask[cell] = 1
	var was_supported := face_supported
	var previous_scale := face_scale
	face_supported = false
	# A shallow puddle needs a smaller face, not a detached rectangular head.
	for scale_value in [1.2, 1.0, 0.85, 0.7, 0.55, 0.4, 0.25, 0.16]:
		if was_supported and scale_value > previous_scale and elapsed < _face_growth_at:
			continue
		var size := Vector2i(maxi(3, roundi(21 * scale_value)), maxi(2, roundi(13 * scale_value)))
		var padding := 2 if scale_value >= 0.4 else 1
		var best_score := INF
		var best := Vector2i.ZERO
		for cell in largest:
			var candidate := Vector2i(cell % WIDTH, cell / WIDTH)
			if not _covered(_face_rect(candidate, size, padding)):
				continue
			var travel := Vector2(candidate).distance_squared_to(tracking_position)
			if travel > 9.0:
				continue
			if was_supported and scale_value > previous_scale and not _covered(_face_rect(candidate, size, padding + 2)):
				continue
			var score := Vector2(candidate).distance_squared_to(center - Vector2(0, 3)) * 0.05 + travel * 0.95
			if score < best_score:
				best_score = score
				best = candidate
		if best_score < INF:
			if scale_value != previous_scale:
				_face_growth_at = elapsed + 0.35
			face_anchor = Vector2(best)
			face_position = tracking_position.lerp(face_anchor, 0.35)
			face_size = size
			face_scale = scale_value
			face_supported = true
			return

	var nearest := INF
	var target := tracking_position
	for cell in largest:
		var candidate := Vector2(cell % WIDTH, cell / WIDTH)
		var distance := candidate.distance_squared_to(tracking_position)
		if distance < nearest:
			nearest = distance
			target = candidate
	face_position = tracking_position + (target - tracking_position).limit_length(2.0)

func _solid_pixel(pixel: Vector2i) -> bool:
	var world := Vector2i((Vector2(pixel) + origin) * body_scale)
	return world.x < 0 or world.y < 0 or world.x >= world_size.x or world.y >= world_size.y or solid[world.y * world_size.x + world.x] != 0

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
