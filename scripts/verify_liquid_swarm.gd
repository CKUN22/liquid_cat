extends SceneTree
## Numerical regression against yesterday's solver, not a visual substitute test.
var failures: Array[String] = []
var results: Array[Dictionary] = []

func _initialize() -> void:
	call_deferred(&"run")

func check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		printerr(message)

func run() -> void:
	var swarm = load("res://scenes/liquid_cat_swarm.tscn").instantiate()
	swarm.CatCount = 1
	swarm.WorldSize = Vector2i(480, 300)
	swarm.RenderEnabled = false
	swarm.KeyboardControl = false
	swarm.UseThreads = false
	root.add_child(swarm)
	swarm.set_physics_process(false)
	var legacy = load("res://scenes/liquid_cat.tscn").instantiate()
	root.add_child(legacy)
	legacy.set_physics_process(false)
	legacy.set_process_unhandled_input(false)
	var lab = load("res://scenes/liquid_cat_lab.tscn").instantiate()
	root.add_child(lab)
	lab.set_physics_process(false)
	lab.set_process_unhandled_input(false)
	lab.cat.set_physics_process(false)
	var spawns := [Vector2(95, 140), Vector2(95, 140), Vector2(232, 120), Vector2(232, 135), Vector2(73, 112), Vector2(240, 118), Vector2(180, 156)]
	for layout in 7:
		lab.set_layout(layout)
		for water in [false, true]:
			swarm.SetObstacles(lab.terrain)
			swarm.ResetCat(0, spawns[layout])
			swarm.SetCatWater(0, water)
			legacy.obstacles = lab.terrain
			legacy.water_mode = water
			legacy.reset(spawns[layout])
			for tick in 30:
				for substep in 3:
					legacy._step(1.0 / 180.0, 0.0)
				swarm.Advance(1.0 / 60.0)
				if tick == 0:
					var first: Dictionary = swarm.Snapshot(0)
					var error := 0.0
					for i in legacy.points.size():
						error = maxf(error, legacy.points[i].distance_to(first.points[i]))
					check(error < 0.01, "first-frame parity layout %d water %s error %f" % [layout, water, error])
			var snapshot: Dictionary = swarm.Snapshot(0)
			var rms := 0.0
			for i in legacy.points.size():
				rms += legacy.points[i].distance_squared_to(snapshot.points[i])
			rms = sqrt(rms / legacy.points.size())
			check(snapshot.finite, "nonfinite in layout %d" % layout)
			check(snapshot.particles == legacy.points.size(), "mass changed")
			check(snapshot.penetrating == 0, "terrain penetration layout %d water %s" % [layout, water])
			check(rms < 2.0, "30-frame parity layout %d water %s RMS %f" % [layout, water, rms])
			results.append({"layout": layout, "water": water, "rms_px": rms, "particles": snapshot.particles})
			swarm.RenderNow()
			var after: Dictionary = swarm.Snapshot(0)
			check(after.points == snapshot.points and after.velocities == snapshot.velocities, "render moved mass")
			swarm.SetCatWater(0, not water)
			var switched: Dictionary = swarm.Snapshot(0)
			check(switched.points == snapshot.points and switched.velocities == snapshot.velocities, "water toggle changed mass/momentum")
	# Water must actually flow through yesterday's 28 px throat, not just fall as a blob.
	lab.set_layout(5)
	swarm.SetObstacles(lab.terrain)
	swarm.ResetCat(0, Vector2(240, 118))
	swarm.SetCatWater(0, true)
	for tick in 600:
		swarm.Advance(1.0 / 60.0)
	var funnel: Dictionary = swarm.Snapshot(0)
	var through := 0
	for p in funnel.points:
		if p.y > 237:
			through += 1
	check(through > 40, "water failed to pass 28 px throat: %d/85" % through)
	check(funnel.finite and funnel.penetrating == 0, "funnel invalid")
	print("FUNNEL particles through=", through)
	# Settled water spreads, splash adds upward velocity, and gather applies force.
	lab.set_layout(3)
	swarm.SetObstacles(lab.terrain)
	swarm.ResetCat(0, Vector2(232, 135))
	swarm.SetCatWater(0, true)
	for tick in 360:
		swarm.Advance(1.0 / 60.0)
	var pool: Dictionary = swarm.Snapshot(0)
	var left := 480.0
	var right := 0.0
	for p in pool.points:
		left = minf(left, p.x)
		right = maxf(right, p.x)
	check(right - left > 65.0, "water did not spread")
	swarm.JumpCat(0)
	swarm.Advance(1.0 / 60.0)
	var splash: Dictionary = swarm.Snapshot(0)
	var upward := 0
	for velocity in splash.velocities:
		if velocity.y < -10:
			upward += 1
	check(upward > 0, "splash lost")
	swarm.SetCatGather(0, true)
	for tick in 60:
		swarm.Advance(1.0 / 60.0)
	check(swarm.Snapshot(0).finite, "gather invalid")
	swarm.SetCatGather(0, false)
	# Measure full old pipeline and new pipeline at identical state, not extrapolated FPS.
	legacy.water_mode = false
	legacy.obstacles = lab.terrain
	legacy.reset(Vector2(232, 135))
	swarm.SetCatWater(0, false)
	swarm.ResetCat(0, Vector2(232, 135))
	legacy.center = legacy._centroid()
	var before := Time.get_ticks_usec()
	for tick in 20:
		legacy._physics_process(1.0 / 60.0)
	var legacy_ms := (Time.get_ticks_usec() - before) / 20000.0
	swarm.RenderEnabled = true
	before = Time.get_ticks_usec()
	for tick in 20:
		swarm.Advance(1.0 / 60.0)
	var swarm_ms := (Time.get_ticks_usec() - before) / 20000.0
	print("PARITY ", JSON.stringify(results))
	print("SINGLE CAT full CPU pipeline ms legacy=", legacy_ms, " gd_swarm=", swarm_ms)
	legacy.queue_free()
	lab.queue_free()
	swarm.queue_free()
	await process_frame
	# 100 real liquids, contact enabled; no rigid bodies, frozen cats, or physics LOD.
	var crowd = load("res://scenes/liquid_cat_swarm.tscn").instantiate()
	crowd.KeyboardControl = false
	root.add_child(crowd)
	crowd.set_physics_process(false)
	var terrain: Array[PackedVector2Array] = [PackedVector2Array([Vector2(0, 850), Vector2(1280, 850), Vector2(1280, 900), Vector2(0, 900)])]
	crowd.SetObstacles(terrain)
	check(crowd.ParticleCount == 100 * results[0].particles, "100 cat particle count")
	var samples: Array[float] = []
	for tick in 180:
		before = Time.get_ticks_usec()
		crowd.Advance(1.0 / 60.0)
		if tick >= 60:
			samples.append((Time.get_ticks_usec() - before) / 1000.0)
	for i in 100:
		var snapshot: Dictionary = crowd.Snapshot(i)
		check(snapshot.finite and snapshot.penetrating == 0, "100 cat invalid state cat %d" % i)
		check(snapshot.particles == results[0].particles, "crowd mass changed")
	crowd.CatCount = 24
	crowd.Advance(1.0 / 60.0)
	check(crowd.Metrics().cats == 24, "count 24")
	crowd.CatCount = 0
	crowd.Advance(1.0 / 60.0)
	check(crowd.ParticleCount == 0, "count zero")
	crowd.CatCount = 100
	crowd.Advance(1.0 / 60.0)
	check(crowd.Metrics().cats == 100, "count restore")
	samples.sort()
	print("100 CAT PIPELINE p50_ms=", samples[samples.size()/2], " p95_ms=", samples[int(samples.size()*0.95)], " metrics=", JSON.stringify(crowd.Metrics()))
	print("LIQUID SWARM ", "PASS" if failures.is_empty() else "FAIL", " failures=", failures)
	crowd.queue_free()
	quit(0 if failures.is_empty() else 1)
