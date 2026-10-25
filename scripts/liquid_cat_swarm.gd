extends Node2D
## GDScript liquid swarm. PascalCase exports/methods preserve existing scene APIs.
const Surface = preload("res://scripts/liquid_cat_surface.gd")
const H := 13.0
const SPACING := 5.4
const RADIUS := 2.3
const SUBSTEPS := 3
const ITERATIONS := 2
@export_range(0, 200, 1) var CatCount := 100
@export var WorldSize := Vector2i(1280, 900)
@export var SpawnRect := Rect2(60, 100, 1120, 640)
@export var Gravity := 260.0
@export var Viscosity := 5.0
@export var Cohesion := 24.0
@export var WaterMode := false
@export var SimulationEnabled := true
@export var RenderEnabled := true
@export var UseThreads := true
@export var MergeOnContact := false:
	set(value):
		if MergeOnContact == value: return
		MergeOnContact = value
		if is_node_ready(): ResetPopulation()
@export var InterCatContacts := true
@export var KeyboardControl := true
@export var AutonomousBehavior := false
@export_range(1, 20, 1) var BehaviorHz := 5.0
signal CatInteracted(cat_id: int)

class CatState extends RefCounted:
	var p := PackedVector2Array()
	var v := PackedVector2Array()
	var before := PackedVector2Array()
	var attraction := PackedVector2Array()
	var contact_delta := PackedVector2Array()
	var contact_velocity := PackedVector2Array()
	var density := PackedFloat32Array()
	var heads := PackedInt32Array()
	var next := PackedInt32Array()
	var pairs := PackedVector3Array()
	var center := Vector2.ZERO
	var axes := Vector2.ZERO
	var mass := 1
	var body_scale := 1.0
	var grounded := false
	var water := false
	var jump := false
	var gather := false
	var dragging := false
	var drag_target := Vector2.ZERO
	var elapsed := 0.0
	var drift := 0.0
	var decision_left := 0.0
	var reaction_left := 0.0
	var behavior_clock := 0.0
	var state := "idle"
	var id := 0
	var rng := RandomNumberGenerator.new()
	var raster = Surface.new()
	func _init(count: int, cells: int, identity: int) -> void:
		id = identity
		rng.seed = id * 7919 + 37
		decision_left = rng.randf() * 3.0
		p.resize(count); v.resize(count); before.resize(count); attraction.resize(count)
		contact_delta.resize(count); contact_velocity.resize(count)
		density.resize(count); heads.resize(cells); next.resize(count)

var _worker_count := clampi(OS.get_processor_count() - 2, 1, 8)
var _cats: Array[CatState] = []
var _rest := PackedVector2Array()
var _terrain: Array[Dictionary] = []
var _solid := PackedByteArray()
var _body_owner := PackedInt32Array()
var _cols := 0
var _rows := 0
var _selected := -1
var _next_id := 0
var _last_requested_count := -1
var _last_water := false
var _drag_offset := Vector2.ZERO
var _global_heads := PackedInt32Array()
var _global_next := PackedInt32Array()
var _particle_cats: Array[CatState] = []
var _particle_indices := PackedInt32Array()
var PhysicsMilliseconds := 0.0
var SolverMilliseconds := 0.0
var ContactMilliseconds := 0.0
var MergeMilliseconds := 0.0
var RasterMilliseconds := 0.0
var UploadMilliseconds := 0.0
var ParticleCount: int:
	get: return _cats.size() * _rest.size()
var SelectedCat: int:
	get: return _selected

func _ready() -> void:
	WorldSize = WorldSize.max(Vector2i(64, 128))
	_cols = ceili(WorldSize.x / H) + 2
	_rows = ceili(WorldSize.y / H) + 2
	_solid.resize(WorldSize.x * WorldSize.y)
	_body_owner.resize(_solid.size())
	_global_heads.resize(_cols * _rows)
	for row in range(-5, 6):
		for col in range(-5, 6):
			var offset := Vector2((col + 0.5 * (row & 1)) * SPACING, row * SPACING * 0.87)
			if offset.length() < 27: _rest.append(offset)
	_last_water = WaterMode
	_sync_count()

func _sync_count() -> void:
	CatCount = clampi(CatCount, 0, 200)
	if _last_requested_count != CatCount:
		for cat in _cats:
			if cat.mass > 1:
				_cats.clear(); _selected = -1
				break
	_last_requested_count = CatCount
	var population := 0
	for cat in _cats: population += cat.mass
	while population > CatCount:
		population -= _cats.back().mass
		_cats.pop_back()
	if _selected >= _cats.size(): _selected = -1
	var columns := maxi(1, int(SpawnRect.size.x / 66))
	while population < CatCount:
		var index := _cats.size()
		var cat := CatState.new(_rest.size(), _cols * _rows, _next_id)
		_next_id += 1
		cat.water = WaterMode
		_cats.append(cat); population += 1
		_reset(cat, SpawnRect.position + Vector2(30 + index % columns * 66, 30 + (index / columns) * 60))
	_rebuild_particle_map()

func _rebuild_particle_map() -> void:
	_global_next.resize(ParticleCount)
	_particle_indices.resize(ParticleCount)
	_particle_cats.resize(ParticleCount)
	for c in _cats.size():
		for i in _rest.size():
			var id := c * _rest.size() + i
			_particle_cats[id] = _cats[c]
			_particle_indices[id] = i

func ResetPopulation() -> void:
	_cats.clear(); _selected = -1
	_sync_count()

func _centroid(cat: CatState) -> Vector2:
	var total := Vector2.ZERO
	for p in cat.p: total += p
	return total / cat.p.size()

func _reset(cat: CatState, origin: Vector2) -> void:
	for i in cat.p.size():
		cat.p[i] = origin + _rest[i]
		cat.v[i] = Vector2.ZERO
	cat.center = _centroid(cat)
	cat.raster.reset_tracking(cat.center / cat.body_scale)
	cat.dragging = false; cat.drift = 0.0

func ResetCat(index: int, origin: Vector2) -> void: _reset(_cats[index], origin)
func SetCatWater(index: int, enabled: bool) -> void: _cats[index].water = enabled
func SetCatAxes(index: int, axes: Vector2) -> void: _cats[index].axes = axes
func SetCatGather(index: int, gather: bool) -> void: _cats[index].gather = gather
func JumpCat(index: int) -> void: _cats[index].jump = true

func SetObstacles(polygons: Array) -> void:
	_terrain.clear()
	_solid.fill(0)
	for polygon in polygons:
		assert(polygon.size() >= 3, "Terrain needs at least 3 vertices")
		var bounds := Rect2(polygon[0], Vector2.ZERO)
		var normals := PackedVector2Array()
		for j in polygon.size():
			bounds = bounds.expand(polygon[j])
			var edge: Vector2 = polygon[(j + 1) % polygon.size()] - polygon[j]
			normals.append(Vector2(edge.y, -edge.x).normalized())
		_terrain.append({"polygon": polygon, "bounds": bounds, "normals": normals})
		for y in range(maxi(0, floori(bounds.position.y)), mini(WorldSize.y - 1, ceili(bounds.end.y)) + 1):
			for x in range(maxi(0, floori(bounds.position.x)), mini(WorldSize.x - 1, ceili(bounds.end.x)) + 1):
				if Geometry2D.is_point_in_polygon(Vector2(x, y), polygon): _solid[y * WorldSize.x + x] = 1

func _collide(cat: CatState, p: Vector2) -> Vector2:
	var radius := RADIUS * cat.body_scale
	for terrain in _terrain:
		if not terrain.bounds.grow(radius).has_point(p): continue
		var inside := Geometry2D.is_point_in_polygon(p, terrain.polygon)
		var best := INF
		var nearest := Vector2.ZERO
		var normal := Vector2.UP
		for edge in terrain.polygon.size():
			var candidate := Geometry2D.get_closest_point_to_segment(p, terrain.polygon[edge], terrain.polygon[(edge + 1) % terrain.polygon.size()])
			var distance := p.distance_squared_to(candidate)
			if distance < best:
				best = distance; nearest = candidate; normal = terrain.normals[edge]
		if inside:
			p = nearest + normal * radius
			if normal.y < -0.4: cat.grounded = true
		elif best < radius * radius:
			var outward := (p - nearest).normalized()
			p = nearest + outward * radius
			if outward.y < -0.4: cat.grounded = true
	return p.clamp(Vector2(12 + radius - RADIUS, 82 + radius - RADIUS), Vector2(WorldSize.x - 12 - radius + RADIUS, WorldSize.y - 35 - radius + RADIUS))

func _cell(p: Vector2) -> int:
	return clampi(floori(p.y / H) + 1, 1, _rows - 2) * _cols + clampi(floori(p.x / H) + 1, 1, _cols - 2)

func _neighbors(cat: CatState) -> void:
	cat.pairs.clear(); cat.density.fill(0); cat.heads.fill(-1)
	for i in range(cat.p.size() - 1, -1, -1):
		var cell := _cell(cat.p[i] / cat.body_scale)
		cat.next[i] = cat.heads[cell]; cat.heads[cell] = i
	var reach_squared := H * H * cat.body_scale * cat.body_scale
	for i in cat.p.size():
		var cell := _cell(cat.p[i] / cat.body_scale)
		var candidates := PackedInt32Array()
		for y in range(-1, 2):
			for x in range(-1, 2):
				var j := cat.heads[cell + y * _cols + x]
				while j >= 0:
					if j > i and cat.p[i].distance_squared_to(cat.p[j]) < reach_squared: candidates.append(j)
					j = cat.next[j]
		candidates.sort()
		for j in candidates:
			var q := 1.0 - cat.p[i].distance_to(cat.p[j]) / (H * cat.body_scale)
			cat.density[i] += q * q; cat.density[j] += q * q
			cat.pairs.append(Vector3(i, j, q))

func _step(cat: CatState, dt: float) -> void:
	cat.before = cat.p.duplicate()
	var com := _centroid(cat)
	var mean := Vector2.ZERO
	for i in cat.p.size():
		var offset := com - cat.p[i]
		var strength := 0.0 if cat.water else Cohesion
		if cat.gather: strength = Cohesion * 2.5
		var force := offset * strength
		if not cat.water and cat.axes.y > 0: force += Vector2(-offset.x * 12, offset.y * 55)
		cat.attraction[i] = force; mean += force
	mean /= cat.p.size()
	for i in cat.p.size():
		var acceleration := Vector2(cat.axes.x * 210, Gravity) + cat.attraction[i] - mean
		if cat.water: acceleration.y += cat.axes.y * 420
		if cat.dragging: acceleration += ((cat.drag_target - com) * 38 - cat.v[i] * 7).limit_length(2400)
		cat.v[i] = (cat.v[i] + acceleration * dt).limit_length(240)
		cat.p[i] += cat.v[i] * dt
	for iteration in ITERATIONS:
		_neighbors(cat)
		var internal_center := _centroid(cat)
		for pair in cat.pairs:
			var i := int(pair.x)
			var j := int(pair.y)
			var offset := cat.p[j] - cat.p[i]
			var distance := offset.length() / cat.body_scale
			if distance < 0.001: continue
			var q := maxf(0, 1.0 - distance / H)
			var pressure := (cat.density[i] + cat.density[j] - 5.8) * 0.22
			var separation := maxf(0, SPACING * 0.88 - distance) * 0.36
			var correction := offset / maxf(distance * cat.body_scale, 0.001) * cat.body_scale * clampf(pressure * q + q * q * 0.35 + separation, 0.0 if cat.water else -0.18, 0.65)
			cat.p[i] -= correction; cat.p[j] += correction
		cat.drift = maxf(cat.drift, internal_center.distance_to(_centroid(cat)))
		for i in cat.p.size(): cat.p[i] = _collide(cat, cat.p[i])
	var damping := exp(-0.35 * dt)
	for i in cat.p.size(): cat.v[i] = (cat.p[i] - cat.before[i]) / dt * damping
	for pair in cat.pairs:
		var i := int(pair.x)
		var j := int(pair.y)
		var impulse := (cat.v[j] - cat.v[i]) * minf(0.16, (0.65 if cat.water else Viscosity) * dt * pair.z)
		cat.v[i] += impulse; cat.v[j] -= impulse

func _contacts() -> void:
	var n := _rest.size()
	for iteration in ITERATIONS:
		_global_heads.fill(-1)
		for c in range(_cats.size() - 1, -1, -1):
			for i in range(n - 1, -1, -1):
				var id := c * n + i
				var cell := _cell(_cats[c].p[i])
				_global_next[id] = _global_heads[cell]; _global_heads[cell] = id
		for a in _cats:
			a.contact_delta.fill(Vector2.ZERO); a.contact_velocity.fill(Vector2.ZERO)
			for i in n:
				var cell := _cell(a.p[i])
				for y in range(-1, 2):
					for x in range(-1, 2):
						var id := _global_heads[cell + y * _cols + x]
						while id >= 0:
							var b := _particle_cats[id]
							var j := _particle_indices[id]
							if a != b:
								var offset := b.p[j] - a.p[i]
								var d2 := offset.length_squared()
								var target := SPACING * 0.88
								if d2 < target * target:
									var distance := sqrt(d2)
									var normal := offset / distance if distance > 0.0001 else Vector2(1 if a.id < b.id else -1, 0)
									a.contact_delta[i] -= normal * minf(0.65, (target - distance) * 0.36)
									var closing := (b.v[j] - a.v[i]).dot(normal)
									if closing < 0: a.contact_velocity[i] += normal * closing * 0.5
							id = _global_next[id]
		for cat in _cats:
			for i in n:
				cat.p[i] = _collide(cat, cat.p[i] + cat.contact_delta[i])
				cat.v[i] += cat.contact_velocity[i]

func _bounds(cat: CatState) -> Rect2:
	var bounds := Rect2(cat.p[0], Vector2.ZERO)
	for p in cat.p: bounds = bounds.expand(p)
	return bounds

func _solid_at(p: Vector2) -> bool:
	var x := int(p.x)
	var y := int(p.y)
	return x < 0 or y < 0 or x >= WorldSize.x or y >= WorldSize.y or _solid[y * WorldSize.x + x] != 0

func _touching(a: CatState, b: CatState, a_bounds: Rect2, b_bounds: Rect2) -> bool:
	var reach := SPACING * 0.44 * (a.body_scale + b.body_scale) + 0.8
	if not a_bounds.grow(reach).intersects(b_bounds, true): return false
	for p in a.p:
		for q in b.p:
			if p.distance_squared_to(q) > reach * reach: continue
			var blocked := false
			var steps := maxi(1, ceili(p.distance_to(q) / 2))
			for step in steps + 1:
				if _solid_at(p.lerp(q, float(step) / steps)):
					blocked = true; break
			if not blocked: return true
	return false

func _root(parent: PackedInt32Array, index: int) -> int:
	while parent[index] != index: index = parent[index]
	return index

func _merge_touching() -> void:
	var count := _cats.size()
	if count < 2: return
	var parent := PackedInt32Array()
	var bounds: Array[Rect2] = []
	for i in count:
		parent.append(i); bounds.append(_bounds(_cats[i]))
	var changed := false
	for a in count:
		for b in range(a + 1, count):
			if _root(parent, a) == _root(parent, b) or not _touching(_cats[a], _cats[b], bounds[a], bounds[b]): continue
			parent[_root(parent, b)] = _root(parent, a); changed = true
	if not changed: return
	var selected: CatState = _cats[_selected] if _selected >= 0 else null
	var result: Array[CatState] = []
	var new_selected: CatState
	for root in count:
		if _root(parent, root) != root: continue
		var members: Array[CatState] = []
		for i in count:
			if _root(parent, i) == root: members.append(_cats[i])
		if members.size() == 1:
			result.append(members[0])
			if members[0] == selected: new_selected = selected
			continue
		var survivor: CatState = selected if selected != null and members.has(selected) else members[0]
		var merged := _merge_group(members, survivor)
		if survivor == selected: new_selected = merged
		result.append(merged)
	_cats = result
	_selected = _cats.find(new_selected)
	_rebuild_particle_map()

func _merge_group(members: Array[CatState], survivor: CatState) -> CatState:
	var mass := 0
	var center := Vector2.ZERO
	var velocity := Vector2.ZERO
	for cat in members:
		mass += cat.mass; center += _centroid(cat) * cat.mass
		var mean := Vector2.ZERO
		for v in cat.v: mean += v
		velocity += mean / cat.v.size() * cat.mass
	center /= mass; velocity /= mass
	var merged := CatState.new(_rest.size(), _cols * _rows, survivor.id)
	merged.mass = mass; merged.body_scale = sqrt(mass); merged.water = survivor.water
	merged.axes = survivor.axes; merged.gather = survivor.gather; merged.dragging = survivor.dragging
	merged.drag_target = survivor.drag_target; merged.state = survivor.state; merged.elapsed = survivor.elapsed
	for cat in members: merged.grounded = merged.grounded or cat.grounded
	var member := 0
	var begin := 0.0
	var end := float(members[0].mass)
	for i in merged.p.size():
		var sample := (i + 0.5) * mass / merged.p.size()
		while sample >= end and member < members.size() - 1:
			begin = end; member += 1; end += members[member].mass
		var source := members[member]
		var index := clampi(int((sample - begin) / source.mass * source.p.size()), 0, source.p.size() - 1)
		merged.p[i] = source.p[index]; merged.v[i] = source.v[index]
	var shift := center - _centroid(merged)
	var sampled_velocity := Vector2.ZERO
	for v in merged.v: sampled_velocity += v
	sampled_velocity /= merged.v.size()
	for i in merged.p.size():
		merged.p[i] = _collide(merged, merged.p[i] + shift)
		merged.v[i] += velocity - sampled_velocity
	merged.center = _centroid(merged)
	merged.raster.reset_tracking(merged.center / merged.body_scale)
	return merged

func _behavior(cat: CatState, delta: float) -> void:
	cat.behavior_clock += delta
	if cat.behavior_clock < 1.0 / BehaviorHz: return
	var dt := cat.behavior_clock
	cat.behavior_clock = 0.0
	if cat.dragging: cat.state = "drag"; return
	if cat.reaction_left > 0:
		cat.reaction_left -= dt; cat.state = "react"; return
	cat.decision_left -= dt
	if cat.decision_left <= 0:
		var walk := cat.state != "walk"
		cat.state = "walk" if walk else "idle"
		cat.decision_left = (1.2 if walk else 2.5) * cat.rng.randf_range(0.7, 1.3)
		cat.axes = Vector2(-0.15 if cat.rng.randi_range(0, 1) == 0 else 0.15, 0) if walk else Vector2.ZERO

func _physics_process(delta: float) -> void: _simulate(delta, false)
func _process(_delta: float) -> void:
	if RenderEnabled: RenderNow()
func Advance(delta: float) -> void: _simulate(delta, true)
func _step_index(index: int, dt: float) -> void: _step(_cats[index], dt)
func _simulate(delta: float, render: bool) -> void:
	_sync_count()
	if _last_water != WaterMode:
		for cat in _cats: cat.water = WaterMode
		_last_water = WaterMode
	if _selected >= 0:
		var selected := _cats[_selected]
		selected.drag_target = get_local_mouse_position() + _drag_offset
		if KeyboardControl:
			selected.axes = Vector2(int(Input.is_physical_key_pressed(KEY_D) or Input.is_physical_key_pressed(KEY_RIGHT)) - int(Input.is_physical_key_pressed(KEY_A) or Input.is_physical_key_pressed(KEY_LEFT)), int(Input.is_physical_key_pressed(KEY_S) or Input.is_physical_key_pressed(KEY_DOWN)) - int(Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP)))
			selected.gather = Input.is_physical_key_pressed(KEY_E)
	var start := Time.get_ticks_usec()
	SolverMilliseconds = 0.0; ContactMilliseconds = 0.0; MergeMilliseconds = 0.0
	if SimulationEnabled and delta > 0:
		for cat in _cats:
			cat.elapsed += delta
			if AutonomousBehavior and (not KeyboardControl or _selected < 0 or cat != _cats[_selected]): _behavior(cat, delta)
			if cat.jump:
				for i in cat.p.size():
					if cat.water:
						var dx := cat.p[i].x - cat.center.x
						cat.v[i] += Vector2(dx * 1.5, -115 * exp(-dx * dx / 150))
					elif cat.grounded: cat.v[i].y -= 130
				cat.jump = false
			cat.grounded = false
		var dt := minf(delta, 1.0 / 30.0) / SUBSTEPS
		for substep in SUBSTEPS:
			var solver_start := Time.get_ticks_usec()
			if UseThreads and _cats.size() >= 8:
				var task := WorkerThreadPool.add_group_task(_step_index.bind(dt), _cats.size(), _worker_count)
				WorkerThreadPool.wait_for_group_task_completion(task)
			else:
				for cat in _cats: _step(cat, dt)
			SolverMilliseconds += (Time.get_ticks_usec() - solver_start) / 1000.0
			var contact_start := Time.get_ticks_usec()
			if MergeOnContact:
				_merge_touching()
				MergeMilliseconds += (Time.get_ticks_usec() - contact_start) / 1000.0
			elif InterCatContacts and _cats.size() > 1: _contacts()
			ContactMilliseconds += (Time.get_ticks_usec() - contact_start) / 1000.0
	for cat in _cats: cat.center = _centroid(cat)
	PhysicsMilliseconds = (Time.get_ticks_usec() - start) / 1000.0
	if RenderEnabled and render: RenderNow()

func _prepare_index(index: int) -> void:
	var cat := _cats[index]
	cat.raster.prepare(cat.p, cat.center, cat.body_scale, cat.water, cat.elapsed, WorldSize, _solid)

func _paint_index(index: int) -> void:
	var cat := _cats[index]
	cat.raster.paint(_body_owner, WorldSize, cat.id, cat.body_scale, MergeOnContact)

func _each_raster(action: Callable) -> void:
	if UseThreads and _cats.size() >= 8:
		var task := WorkerThreadPool.add_group_task(action, _cats.size(), _worker_count)
		WorkerThreadPool.wait_for_group_task_completion(task)
	else:
		for i in _cats.size(): action.call(i)

func RenderNow() -> void:
	var start := Time.get_ticks_usec()
	_body_owner.fill(-1)
	_each_raster(_prepare_index)
	if not MergeOnContact:
		for cat in _cats: cat.raster.mark_owner(_body_owner, WorldSize, cat.id, cat.body_scale)
	_each_raster(_paint_index)
	RasterMilliseconds = (Time.get_ticks_usec() - start) / 1000.0
	start = Time.get_ticks_usec()
	for cat in _cats: cat.raster.upload()
	UploadMilliseconds = (Time.get_ticks_usec() - start) / 1000.0
	queue_redraw()

func _draw() -> void:
	for cat in _cats:
		if cat.raster.surface != null:
			draw_texture_rect(cat.raster.surface, Rect2(cat.raster.origin * cat.body_scale, Vector2(cat.raster.WIDTH, cat.raster.HEIGHT) * cat.body_scale), false)
	for cat in _cats:
		if cat.raster.features_surface != null:
			draw_texture_rect(cat.raster.features_surface, Rect2(cat.raster.origin * cat.body_scale, Vector2(cat.raster.WIDTH, cat.raster.HEIGHT) * cat.body_scale), false)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
			for cat in _cats:
				cat.dragging = false
				if cat.state == "drag": cat.state = "idle"
		elif event.pressed and event.button_index in [MOUSE_BUTTON_LEFT, MOUSE_BUTTON_RIGHT]:
			var point: Vector2 = get_global_transform_with_canvas().affine_inverse() * event.position
			var hit := -1
			var best := 144.0
			for c in range(_cats.size() - 1, -1, -1):
				for p in _cats[c].p:
					var distance := p.distance_squared_to(point) / pow(_cats[c].body_scale, 2)
					if distance < best: best = distance; hit = c
			if hit >= 0:
				if _selected >= 0:
					_cats[_selected].axes = Vector2.ZERO; _cats[_selected].gather = false; _cats[_selected].dragging = false
				_selected = hit
				if event.button_index == MOUSE_BUTTON_LEFT:
					_cats[hit].dragging = true; _cats[hit].state = "drag"; _drag_offset = _cats[hit].center - point
				else:
					_cats[hit].jump = true; _cats[hit].reaction_left = 0.8; _cats[hit].state = "react"
				CatInteracted.emit(_cats[hit].id)
				get_viewport().set_input_as_handled()
	if event is InputEventKey and event.pressed and not event.echo and _selected >= 0:
		if event.physical_keycode in [KEY_SPACE, KEY_W, KEY_UP]: _cats[_selected].jump = true
		if event.physical_keycode == KEY_F: _cats[_selected].water = not _cats[_selected].water

func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		for cat in _cats:
			cat.dragging = false; cat.axes = Vector2.ZERO; cat.gather = false

func Snapshot(index: int) -> Dictionary:
	var cat := _cats[index]
	var finite := true
	var penetrating := 0
	for p in cat.p:
		finite = finite and p.is_finite()
		for terrain in _terrain:
			if Geometry2D.is_point_in_polygon(p, terrain.polygon): penetrating += 1; break
	for v in cat.v: finite = finite and v.is_finite()
	return {"cat_id": cat.id, "points": cat.p.duplicate(), "velocities": cat.v.duplicate(), "center": cat.center, "particles": cat.p.size(), "finite": finite, "penetrating": penetrating, "mass_units": cat.mass, "body_scale": cat.body_scale, "particle_mass": float(cat.mass) / cat.p.size(), "water": cat.water, "face_supported": cat.raster.face_supported, "face_anchor": (cat.raster.face_anchor + cat.raster.origin) * cat.body_scale, "face_size": cat.raster.face_size, "internal_center_drift": cat.drift, "state": cat.state}

func Metrics() -> Dictionary:
	var bytes := 0
	var mass := 0
	for cat in _cats:
		mass += cat.mass
		bytes += cat.raster.WIDTH * cat.raster.HEIGHT * 8
	return {"cats": _cats.size(), "mass_units": mass, "merge_ms": MergeMilliseconds, "particles": ParticleCount, "physics_ms": PhysicsMilliseconds, "solver_ms": SolverMilliseconds, "contacts_ms": ContactMilliseconds, "workers": _worker_count if UseThreads and _cats.size() >= 8 else 1, "raster_ms": RasterMilliseconds, "upload_ms": UploadMilliseconds, "texture_bytes": bytes}
