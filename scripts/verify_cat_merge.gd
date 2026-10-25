extends SceneTree
var failures: Array[String] = []
func check(ok: bool, message: String) -> void:
 if not ok: failures.append(message); printerr(message)
func _initialize() -> void: call_deferred("run")
func run() -> void:
 var swarm = load("res://scenes/liquid_cat_swarm.tscn").instantiate()
 swarm.CatCount = 2
 swarm.KeyboardControl = false
 swarm.Gravity = 0
 swarm.MergeOnContact = true
 root.add_child(swarm)
 swarm.set_physics_process(false); swarm.set_process(false)
 swarm.ResetCat(0, Vector2(200,200)); swarm.ResetCat(1, Vector2(400,200))
 swarm.Advance(1.0/60)
 check(swarm.Metrics().cats == 2, "Separated cats merged")
 swarm.ResetCat(1, Vector2(248,200))
 for tick in 8: swarm.Advance(1.0/60)
 check(swarm.Metrics().cats == 1, "Touching cats did not merge")
 check(swarm.Metrics().mass_units == 2, "Merge lost volume units")
 check(swarm.ParticleCount == 85, "Merge did not simplify particles")
 var snap: Dictionary = swarm.Snapshot(0)
 check(abs(snap.body_scale*snap.body_scale-2.0)<0.0001, "2D volume scaling incorrect")
 check(abs(snap.particle_mass*85-2.0)<0.0001, "Particle weights lost mass")
 check(snap.finite and snap.penetrating == 0, "Merged body invalid")
 for tick in 120: swarm.Advance(1.0/60)
 check(swarm.Snapshot(0).face_supported, "Merged body lost its one face")
 swarm.SetCatAxes(0, Vector2(1,0))
 for tick in 60: swarm.Advance(1.0/60)
 check(swarm.Metrics().cats == 1 and swarm.Metrics().mass_units == 2, "Interaction split body")
 swarm.ResetPopulation()
 check(swarm.Metrics().cats == 2 and swarm.ParticleCount == 170, "Reset failed to restore originals")
 # Bodies on opposite sides of a thin wall must not fuse through it.
 var wall: Array[PackedVector2Array] = [PackedVector2Array([Vector2(224,80),Vector2(228,80),Vector2(228,850),Vector2(224,850)])]
 swarm.SetObstacles(wall)
 swarm.ResetCat(0,Vector2(195,200));swarm.ResetCat(1,Vector2(257,200))
 for tick in 30: swarm.Advance(1.0/60)
 check(swarm.Metrics().cats == 2, "Cats merged across terrain")
 swarm.CatCount = 3; swarm.Advance(1.0/60)
 swarm.SetObstacles([])
 for i in 3: swarm.ResetCat(i,Vector2(300+i*45,250))
 for tick in 20: swarm.Advance(1.0/60)
 check(swarm.Metrics().cats == 1 and swarm.Metrics().mass_units == 3, "Chain merge failed")
 swarm.CatCount = 0; swarm.Advance(1.0/60)
 check(swarm.ParticleCount == 0, "Zero count failed after merge")
 swarm.CatCount = 100; swarm.Gravity = 260; swarm.ResetPopulation()
 var floor_poly: Array[PackedVector2Array] = [PackedVector2Array([Vector2(0,850),Vector2(1280,850),Vector2(1280,900),Vector2(0,900)])]
 swarm.SetObstacles(floor_poly)
 for tick in 600: swarm.Advance(1.0/60)
 var m: Dictionary = swarm.Metrics()
 check(m.mass_units == 100, "100-cat merge lost volume")
 check(m.cats < 100 and m.particles < 8500, "100-cat pile did not reduce workload")
 for i in int(m.cats):
  snap = swarm.Snapshot(i)
  check(snap.finite and snap.penetrating == 0, "100-cat merged state invalid")
 print("MERGE_PILE ",JSON.stringify(m))
 swarm.WaterMode = true
 for tick in 180: swarm.Advance(1.0/60)
 check(swarm.Metrics().mass_units == 100, "Water toggle lost mass")
 for i in int(swarm.Metrics().cats): check(swarm.Snapshot(i).finite, "Merged water invalid")
 print("CAT_MERGE ", "PASS" if failures.is_empty() else "FAIL", " failures=",failures)
 swarm.queue_free();quit(0 if failures.is_empty() else 1)
