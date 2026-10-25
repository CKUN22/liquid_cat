using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;


/// <summary>Shared C# liquid-cat solver and raster. Same particles and constitutive law,
/// 3 substeps x 2 constraints; spatial neighbors, cropped raster, shared terrain mask.
/// Optional contact merging replaces a connected group with 85 weighted samples.</summary>
public partial class LiquidCatSwarm : Node2D
{
    const float H = 13f, Spacing = 5.4f, Radius = 2.3f;
    const int Substeps = 3, Iterations = 2;
    static readonly Color Ink = new("414458");
    static readonly Color WaterTint = new("74d4e8"), WaterShade = new("438da9"), WaterHighlight = new("d8fbf4"), WaterSparkle = new("b8f4ef");
    [Export(PropertyHint.Range, "0,200,1")] public int CatCount { get; set; } = 100;
    [Export] public Vector2I WorldSize { get; set; } = new(1280, 900);
    [Export] public Rect2 SpawnRect { get; set; } = new(60, 100, 1120, 640);
    [Export] public float Gravity { get; set; } = 260f;
    [Export] public float Viscosity { get; set; } = 5f;
    [Export] public float Cohesion { get; set; } = 24f;
    [Export] public bool WaterMode { get; set; }
    [Export] public bool SimulationEnabled { get; set; } = true;
    [Export] public bool RenderEnabled { get; set; } = true;
    [Export] public bool UseThreads { get; set; } = true;
    bool _mergeOnContact;
    [Export] public bool MergeOnContact
    {
        get => _mergeOnContact;
        set { if (_mergeOnContact == value) return; _mergeOnContact = value; if (IsNodeReady()) ResetPopulation(); }
    }
    [Export] public bool InterCatContacts { get; set; } = true;
    [Export] public bool DebugParticles { get; set; }
    [Export] public bool KeyboardControl { get; set; } = true;
    [Export] public bool AutonomousBehavior { get; set; }
    [Export(PropertyHint.Range, "1,20,1")] public float BehaviorHz { get; set; } = 5;
    [Signal] public delegate void CatInteractedEventHandler(int catId);

    sealed class Cat
    {
        public FastVector2[] P, V, Rest, Before, Attraction, ContactDelta, ContactVelocity;
        public float[] Density;
        public int[] Next, Heads;
        public readonly List<Pair> Pairs = new(1024);
        public FastVector2 Center, Face, LastCenter, FaceAnchor, Axes;
        public int MassUnits = 1;
        public float Scale = 1;
        public bool Grounded, Water, Jump, Gather, Dragging;
        public FastVector2 DragTarget;
        public float Elapsed, Drift, DecisionLeft, ReactionLeft, BehaviorClock, FaceGrowthAt;
        public string State = "idle";
        public readonly Random Rng;
        public readonly int Id;
        public readonly Raster Raster = new();
        public Color Fur = new("fffdf2"), Shade = new("cbd5df"), Ear = new("e9a6b2");
        public Color[] FacePixels = Array.Empty<Color>();
        public Image? Image;
        public ImageTexture? Texture, FeaturesTexture;
        public Image? FeaturesImage;
        public readonly Raster Features = new();
        public Cat(FastVector2[] rest, int cells, int id)
        {
            Rest = rest; P = new FastVector2[rest.Length]; V = new FastVector2[rest.Length];
            Before = new FastVector2[rest.Length]; Attraction = new FastVector2[rest.Length];
            ContactDelta = new FastVector2[rest.Length]; ContactVelocity = new FastVector2[rest.Length];
            Density = new float[rest.Length]; Next = new int[rest.Length]; Heads = new int[cells];
            Id = id; Rng = new Random(id * 7919 + 37); DecisionLeft = (float)Rng.NextDouble() * 3;
        }
    }
    readonly record struct Pair(int I, int J, float Q);
    sealed class Terrain
    {
        public FastVector2[] Polygon = Array.Empty<FastVector2>();
        public Rect2 Bounds;
        public FastVector2[] Normals = Array.Empty<FastVector2>();
    }
    sealed class Raster
    {
        public int X, Y, W, H, FaceW = 21, FaceH = 13;
        public float FaceScale = 1;
        public bool FaceSupported;
        public float[] Field = Array.Empty<float>();
        public byte[] Mask = Array.Empty<byte>(), Remaining = Array.Empty<byte>(), Feature = Array.Empty<byte>(), Bytes = Array.Empty<byte>();
        public int[] Sum = Array.Empty<int>(), Queue = Array.Empty<int>(), Largest = Array.Empty<int>();
        public int LargestCount;
        public void ResizePixels(int w, int h)
        {
            if (W == w && H == h) return;
            W = w; H = h; Bytes = new byte[w * h * 4];
        }
        public void Resize(int w, int h)
        {
            if (W == w && H == h) return;
            W = w; H = h;
            Field = new float[w * h];
            Mask = new byte[w * h]; Remaining = new byte[w * h]; Feature = new byte[w * h];
            Bytes = new byte[w * h * 4]; Sum = new int[(w + 1) * (h + 1)];
            Queue = new int[w * h]; Largest = new int[w * h];
        }
    }
    readonly List<Cat> _cats = new();
    Terrain[] _terrain = Array.Empty<Terrain>();
    byte[] _solid = Array.Empty<byte>();
    int[] _bodyOwner = Array.Empty<int>();
    FastVector2[] _rest = Array.Empty<FastVector2>();
    int _cols, _rows, _selected = -1, _nextId;
    int[] _globalHeads = Array.Empty<int>(), _globalNext = Array.Empty<int>();
    Cat[] _particleCats = Array.Empty<Cat>();
    int[] _particleIndices = Array.Empty<int>();
    Color[] _whiteFace = Array.Empty<Color>();
    FastVector2 _dragOffset;
    bool _lastWater;
    int _lastRequestedCount = -1;
    public double MergeMilliseconds { get; private set; }
    readonly ParallelOptions _parallel = new() { MaxDegreeOfParallelism = Math.Clamp(System.Environment.ProcessorCount - 2, 1, 8) };
    public double PhysicsMilliseconds { get; private set; }
    public double SolverMilliseconds { get; private set; }
    public double ContactMilliseconds { get; private set; }
    public double RasterMilliseconds { get; private set; }
    public double UploadMilliseconds { get; private set; }
    public int ParticleCount => _cats.Count * _rest.Length;
    public int SelectedCat => _selected;

    public override void _Ready()
    {
        WorldSize = new(Math.Max(64, WorldSize.X), Math.Max(128, WorldSize.Y));
        _cols = (int)MathF.Ceiling(WorldSize.X / H) + 2;
        _rows = (int)MathF.Ceiling(WorldSize.Y / H) + 2;
        _solid = new byte[WorldSize.X * WorldSize.Y];
        _bodyOwner = new int[_solid.Length];
        _globalHeads = new int[_cols * _rows];
        var rest = new List<FastVector2>();
        for (int row = -5; row <= 5; row++) for (int col = -5; col <= 5; col++)
        {
            var offset = new FastVector2((col + 0.5f * (row & 1)) * Spacing, row * Spacing * 0.87f);
            if (offset.Length() < 27) rest.Add(offset);
        }
        _rest = rest.ToArray();
        using (var face = GD.Load<Texture2D>("res://assetsimagecat/face.svg").GetImage())
        {
            if (face.IsCompressed()) face.Decompress();
            _whiteFace = new Color[21 * 13];
            for (int y = 0; y < 13; y++) for (int x = 0; x < 21; x++) _whiteFace[y * 21 + x] = face.GetPixel(x, y);
        }
        _lastWater = WaterMode;
        SyncCount();
    }
    void Each(Action<Cat> action)
    {
        if (UseThreads && _cats.Count >= 8) Parallel.For(0, _cats.Count, _parallel, i => action(_cats[i]));
        else foreach (var cat in _cats) action(cat);
    }
    void SyncCount()
    {
        CatCount = Math.Clamp(CatCount, 0, 200);
        if (_lastRequestedCount != CatCount && _cats.Exists(cat => cat.MassUnits > 1)) ClearPopulation();
        _lastRequestedCount = CatCount;
        int population = 0; foreach (var cat in _cats) population += cat.MassUnits;
        while (population > CatCount)
        {
            var cat = _cats[^1]; population -= cat.MassUnits; cat.Texture?.Dispose(); cat.Image?.Dispose(); cat.FeaturesTexture?.Dispose(); cat.FeaturesImage?.Dispose(); _cats.RemoveAt(_cats.Count - 1);
        }
        if (_selected >= _cats.Count) _selected = -1;
        int columns = Math.Max(1, (int)(SpawnRect.Size.X / 66));
        while (population < CatCount)
        {
            int index = _cats.Count;
            var cat = new Cat(_rest, _cols * _rows, _nextId++) { Water = WaterMode };
            cat.FacePixels = _whiteFace;
            _cats.Add(cat); population++;
            Reset(cat, (FastVector2)SpawnRect.Position + new FastVector2(30 + index % columns * 66, 30 + index / columns * 60));
        }
        RebuildParticleMap();
    }
    void RebuildParticleMap()
    {
        if (_globalNext.Length != ParticleCount)
        {
            _globalNext = new int[ParticleCount]; _particleCats = new Cat[ParticleCount]; _particleIndices = new int[ParticleCount];
            for (int c = 0; c < _cats.Count; c++) for (int i = 0; i < _rest.Length; i++)
            { int id = c * _rest.Length + i; _particleCats[id] = _cats[c]; _particleIndices[id] = i; }
        }
    }
    static void DisposeCat(Cat cat)
    {
        cat.Texture?.Dispose(); cat.Image?.Dispose(); cat.FeaturesTexture?.Dispose(); cat.FeaturesImage?.Dispose();
    }
    void ClearPopulation()
    {
        foreach (var cat in _cats) DisposeCat(cat);
        _cats.Clear(); _selected = -1;
    }
    public void ResetPopulation()
    {
        ClearPopulation(); SyncCount();
    }
    static Rect2 ParticleBounds(Cat cat)
    {
        var bounds = new Rect2(cat.P[0], FastVector2.Zero);
        foreach (var p in cat.P) bounds = bounds.Expand(p);
        return bounds;
    }
    bool Touching(Cat a, Cat b, Rect2 aBounds, Rect2 bBounds)
    {
        // Test the actual bodies, not their centers or overlapping bounding boxes.
        float reach = Spacing * 0.44f * (a.Scale + b.Scale) + 0.8f;
        if (!aBounds.Grow(reach).Intersects(bBounds, true)) return false;
        foreach (var p in a.P) foreach (var q in b.P)
        {
            if (p.DistanceSquaredTo(q) > reach * reach) continue;
            bool blocked = false; int steps = Math.Max(1, (int)MathF.Ceiling(p.DistanceTo(q) / 2));
            for (int step = 0; step <= steps; step++)
            {
                var sample = p.Lerp(q, (float)step / steps);
                if (Solid((int)sample.X, (int)sample.Y)) { blocked = true; break; }
            }
            if (!blocked) return true;
        }
        return false;
    }
    void MergeTouching()
    {
        int count = _cats.Count; if (count < 2) return;
        var parent = new int[count]; var bounds = new Rect2[count];
        for (int i = 0; i < count; i++) { parent[i] = i; bounds[i] = ParticleBounds(_cats[i]); }
        int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        bool changed = false;
        for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
        {
            if (Root(a) == Root(b) || !Touching(_cats[a], _cats[b], bounds[a], bounds[b])) continue;
            parent[Root(b)] = Root(a); changed = true;
        }
        if (!changed) return;
        Cat? selected = _selected >= 0 ? _cats[_selected] : null;
        var result = new List<Cat>(); Cat? newSelected = null;
        for (int root = 0; root < count; root++)
        {
            if (Root(root) != root) continue;
            var members = new List<Cat>();
            for (int i = 0; i < count; i++) if (Root(i) == root) members.Add(_cats[i]);
            if (members.Count == 1) { result.Add(members[0]); if (members[0] == selected) newSelected = selected; continue; }
            var survivor = selected != null && members.Contains(selected) ? selected : members[0];
            var merged = MergeGroup(members, survivor);
            if (survivor == selected) newSelected = merged;
            result.Add(merged);
            foreach (var cat in members) DisposeCat(cat);
        }
        _cats.Clear(); _cats.AddRange(result);
        _selected = newSelected == null ? -1 : _cats.IndexOf(newSelected);
        RebuildParticleMap();
    }
    Cat MergeGroup(List<Cat> members, Cat survivor)
    {
        int mass = 0; FastVector2 center = FastVector2.Zero, velocity = FastVector2.Zero;
        foreach (var cat in members)
        {
            mass += cat.MassUnits; center += Centroid(cat) * cat.MassUnits;
            FastVector2 mean = FastVector2.Zero; foreach (var v in cat.V) mean += v;
            velocity += mean / cat.V.Length * cat.MassUnits;
        }
        center /= mass; velocity /= mass;
        var merged = new Cat(_rest, _cols * _rows, survivor.Id) {
            MassUnits = mass, Scale = MathF.Sqrt(mass), Water = survivor.Water,
            FacePixels = survivor.FacePixels, Fur = survivor.Fur, Shade = survivor.Shade, Ear = survivor.Ear,
            Axes = survivor.Axes, Gather = survivor.Gather, Dragging = survivor.Dragging,
            DragTarget = survivor.DragTarget, State = survivor.State, Elapsed = survivor.Elapsed,
            Grounded = members.Exists(cat => cat.Grounded)
        };
        // Weighted stratified resampling keeps the old spatial distribution; each
        // surviving sample represents mass/85 original-cat particle masses.
        int member = 0; float begin = 0, end = members[0].MassUnits;
        for (int i = 0; i < merged.P.Length; i++)
        {
            float sample = (i + 0.5f) * mass / merged.P.Length;
            while (sample >= end && member < members.Count - 1) { begin = end; member++; end += members[member].MassUnits; }
            var source = members[member];
            int index = Math.Clamp((int)((sample - begin) / source.MassUnits * source.P.Length), 0, source.P.Length - 1);
            merged.P[i] = source.P[index]; merged.V[i] = source.V[index];
        }
        FastVector2 shift = center - Centroid(merged), sampledVelocity = FastVector2.Zero;
        foreach (var v in merged.V) sampledVelocity += v;
        sampledVelocity /= merged.V.Length;
        for (int i = 0; i < merged.P.Length; i++)
        {
            merged.P[i] = Collide(merged, merged.P[i] + shift);
            merged.V[i] += velocity - sampledVelocity;
        }
        merged.Center = Centroid(merged);
        merged.Face = merged.Center / merged.Scale - new FastVector2(0, 2);
        merged.FaceAnchor = merged.Face; merged.LastCenter = merged.Center / merged.Scale;
        return merged;
    }
    static FastVector2 Centroid(Cat cat)
    {
        FastVector2 sum = FastVector2.Zero;
        foreach (var p in cat.P) sum += p;
        return sum / cat.P.Length;
    }
    void Reset(Cat cat, FastVector2 origin)
    {
        for (int i = 0; i < cat.P.Length; i++) { cat.P[i] = origin + cat.Rest[i]; cat.V[i] = FastVector2.Zero; }
        cat.Center = Centroid(cat); cat.Face = cat.Center / cat.Scale - new FastVector2(0, 2);
        cat.FaceAnchor = cat.Face; cat.LastCenter = cat.Center / cat.Scale;
        cat.Dragging = false; cat.Drift = 0;
    }
    public void ResetCat(int index, Godot.Vector2 origin) { Reset(_cats[index], origin); }
    public void SelectCat(int index) { _selected = index >= 0 && index < _cats.Count ? index : -1; }
    public void SetCatAppearance(int index, CatAppearance appearance)
    {
        var cat = _cats[index]; cat.Fur = appearance.Fur; cat.Shade = appearance.Shade; cat.Ear = appearance.Ear;
        using var face = appearance.Face.GetImage();
        if (face.IsCompressed()) face.Decompress();
        cat.FacePixels = new Color[21 * 13];
        for (int y = 0; y < 13; y++) for (int x = 0; x < 21; x++)
            cat.FacePixels[y * 21 + x] = face.GetPixel(Math.Min(x, face.GetWidth() - 1), Math.Min(y, face.GetHeight() - 1));
    }
    protected void BeginDragCat(int index, Godot.Vector2 position)
    {
        _selected = index; var cat = _cats[index]; cat.Dragging = true; cat.State = "drag";
        _dragOffset = cat.Center - (FastVector2)position; EmitSignal(SignalName.CatInteracted, cat.Id);
        GetViewport().SetInputAsHandled();
    }
    public void SetCatWater(int index, bool enabled) { _cats[index].Water = enabled; }
    public void SetCatAxes(int index, Godot.Vector2 axes) { _cats[index].Axes = axes; }
    public void SetCatGather(int index, bool gather) { _cats[index].Gather = gather; }
    public void JumpCat(int index) { _cats[index].Jump = true; }

    public void SetObstacles(Godot.Collections.Array<Godot.Vector2[]> polygons)
    {
        _terrain = new Terrain[polygons.Count];
        for (int i = 0; i < polygons.Count; i++)
        {
            var polygon = Array.ConvertAll(polygons[i], p => (FastVector2)p);
            if (polygon.Length < 3) throw new ArgumentException("Terrain needs at least 3 vertices.");
            var bounds = new Rect2(polygon[0], FastVector2.Zero);
            var normals = new FastVector2[polygon.Length];
            for (int j = 0; j < polygon.Length; j++)
            {
                bounds = bounds.Expand(polygon[j]);
                var edge = polygon[(j + 1) % polygon.Length] - polygon[j];
                normals[j] = new FastVector2(edge.Y, -edge.X).Normalized();
            }
            _terrain[i] = new Terrain { Polygon = polygon, Bounds = bounds, Normals = normals };
        }
        Array.Clear(_solid);
        foreach (var terrain in _terrain)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(terrain.Bounds.Position.X));
            int y0 = Math.Max(0, (int)MathF.Floor(terrain.Bounds.Position.Y));
            int x1 = Math.Min(WorldSize.X - 1, (int)MathF.Ceiling(terrain.Bounds.End.X));
            int y1 = Math.Min(WorldSize.Y - 1, (int)MathF.Ceiling(terrain.Bounds.End.Y));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (Inside(new FastVector2(x, y), terrain.Polygon)) _solid[y * WorldSize.X + x] = 1;
        }
    }
    // Pure managed geometry: workers never call scene, rendering or native APIs.
    static bool Inside(FastVector2 p, FastVector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    static FastVector2 Closest(FastVector2 p, FastVector2 a, FastVector2 b)
    {
        var edge = b - a; float length = edge.LengthSquared();
        return a + edge * (length > 0 ? Math.Clamp((p - a).Dot(edge) / length, 0, 1) : 0);
    }
    FastVector2 Collide(Cat cat, FastVector2 p)
    {
        float radius = Radius * cat.Scale;
        foreach (var terrain in _terrain)
        {
            if (!terrain.Bounds.Grow(radius).HasPoint(p)) continue;
            bool inside = Inside(p, terrain.Polygon);
            float best = float.PositiveInfinity; FastVector2 nearest = FastVector2.Zero, normal = FastVector2.Up;
            for (int edge = 0; edge < terrain.Polygon.Length; edge++)
            {
                var candidate = Closest(p, terrain.Polygon[edge], terrain.Polygon[(edge + 1) % terrain.Polygon.Length]);
                float distance = p.DistanceSquaredTo(candidate);
                if (distance < best) { best = distance; nearest = candidate; normal = terrain.Normals[edge]; }
            }
            if (inside)
            {
                p = nearest + normal * radius;
                if (normal.Y < -0.4f) cat.Grounded = true;
            }
            else if (best < radius * radius)
            {
                var outward = (p - nearest).Normalized(); p = nearest + outward * radius;
                if (outward.Y < -0.4f) cat.Grounded = true;
            }
        }
        return new FastVector2(Math.Clamp(p.X, 12 + radius - Radius, WorldSize.X - 12 - radius + Radius), Math.Clamp(p.Y, 82 + radius - Radius, WorldSize.Y - 35 - radius + Radius));
    }
    int Cell(FastVector2 p) => Math.Clamp((int)MathF.Floor(p.Y / H) + 1, 1, _rows - 2) * _cols + Math.Clamp((int)MathF.Floor(p.X / H) + 1, 1, _cols - 2);
    int CatCell(Cat cat, FastVector2 p) => Cell(p / cat.Scale);
    void Neighbors(Cat cat)
    {
        cat.Pairs.Clear(); Array.Clear(cat.Density); Array.Fill(cat.Heads, -1);
        for (int i = cat.P.Length - 1; i >= 0; i--)
        {
            int cell = CatCell(cat, cat.P[i]); cat.Next[i] = cat.Heads[cell]; cat.Heads[cell] = i;
        }
        for (int i = 0; i < cat.P.Length; i++)
        {
            int cell = CatCell(cat, cat.P[i]); ulong low = 0, high = 0;
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                for (int j = cat.Heads[cell + y * _cols + x]; j >= 0; j = cat.Next[j])
                    if (j > i && cat.P[i].DistanceSquaredTo(cat.P[j]) < H * H * cat.Scale * cat.Scale)
                    {
                        if (j < 64) low |= 1UL << j; else high |= 1UL << (j - 64);
                    }
            // Keep the original solver's i/j correction order for reproducibility.
            while (low != 0 || high != 0)
            {
                int j;
                if (low != 0) { j = BitOperations.TrailingZeroCount(low); low &= low - 1; }
                else { j = 64 + BitOperations.TrailingZeroCount(high); high &= high - 1; }
                float q = 1 - cat.P[i].DistanceTo(cat.P[j]) / (H * cat.Scale);
                cat.Density[i] += q * q; cat.Density[j] += q * q; cat.Pairs.Add(new Pair(i, j, q));
            }
        }
    }
    void Step(Cat cat, float dt)
    {
        Array.Copy(cat.P, cat.Before, cat.P.Length);
        var com = Centroid(cat); var mean = FastVector2.Zero;
        for (int i = 0; i < cat.P.Length; i++)
        {
            var offset = com - cat.P[i]; float strength = cat.Water ? 0 : Cohesion;
            if (cat.Gather) strength = Cohesion * 2.5f;
            var force = offset * strength;
            if (!cat.Water && cat.Axes.Y > 0) force += new FastVector2(-offset.X * 12, offset.Y * 55);
            cat.Attraction[i] = force; mean += force;
        }
        mean /= cat.P.Length;
        for (int i = 0; i < cat.P.Length; i++)
        {
            var acceleration = new FastVector2(cat.Axes.X * 210, Gravity) + cat.Attraction[i] - mean;
            if (cat.Water) acceleration.Y += cat.Axes.Y * 420;
            if (cat.Dragging) acceleration += ((cat.DragTarget - com) * 38 - cat.V[i] * 7).LimitLength(2400);
            cat.V[i] = (cat.V[i] + acceleration * dt).LimitLength(240); cat.P[i] += cat.V[i] * dt;
        }
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            Neighbors(cat); var internalCenter = Centroid(cat);
            foreach (var pair in cat.Pairs)
            {
                int i = pair.I, j = pair.J; var offset = cat.P[j] - cat.P[i]; float distance = offset.Length() / cat.Scale;
                if (distance < 0.001f) continue;
                float q = Math.Max(0, 1 - distance / H);
                float pressure = (cat.Density[i] + cat.Density[j] - 5.8f) * 0.22f;
                float separation = Math.Max(0, Spacing * 0.88f - distance) * 0.36f;
                var correction = offset / Math.Max(distance * cat.Scale, 0.001f) * cat.Scale * Math.Clamp(pressure * q + q * q * 0.35f + separation, cat.Water ? 0 : -0.18f, 0.65f);
                cat.P[i] -= correction; cat.P[j] += correction;
            }
            cat.Drift = Math.Max(cat.Drift, internalCenter.DistanceTo(Centroid(cat)));
            for (int i = 0; i < cat.P.Length; i++) cat.P[i] = Collide(cat, cat.P[i]);
        }
        float damping = MathF.Exp(-0.35f * dt);
        for (int i = 0; i < cat.P.Length; i++) cat.V[i] = (cat.P[i] - cat.Before[i]) / dt * damping;
        foreach (var pair in cat.Pairs)
        {
            var impulse = (cat.V[pair.J] - cat.V[pair.I]) * Math.Min(0.16f, (cat.Water ? 0.65f : Viscosity) * dt * pair.Q);
            cat.V[pair.I] += impulse; cat.V[pair.J] -= impulse;
        }
    }
    void Contacts()
    {
        int n = _rest.Length;
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            Array.Fill(_globalHeads, -1);
            for (int c = _cats.Count - 1; c >= 0; c--) for (int i = n - 1; i >= 0; i--)
            {
                int id = c * n + i, cell = Cell(_cats[c].P[i]);
                _globalNext[id] = _globalHeads[cell]; _globalHeads[cell] = id;
            }
            // Jacobi: immutable position/velocity snapshot, own output buffers only.
            Each(a =>
            {
                Array.Clear(a.ContactDelta); Array.Clear(a.ContactVelocity);
                for (int i = 0; i < n; i++)
                {
                    int cell = Cell(a.P[i]);
                    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                    for (int id = _globalHeads[cell + y * _cols + x]; id >= 0; id = _globalNext[id])
                    {
                        var b = _particleCats[id]; if (a == b) continue;
                        int j = _particleIndices[id];
                        var offset = b.P[j] - a.P[i]; float d2 = offset.LengthSquared(), target = Spacing * 0.88f;
                        if (d2 >= target * target) continue;
                        float distance = MathF.Sqrt(d2);
                        var normal = distance > 0.0001f ? offset / distance : new FastVector2(a.Id < b.Id ? 1 : -1, 0);
                        a.ContactDelta[i] -= normal * Math.Min(0.65f, (target - distance) * 0.36f);
                        float closing = (b.V[j] - a.V[i]).Dot(normal);
                        if (closing < 0) a.ContactVelocity[i] += normal * closing * 0.5f;
                    }
                }
            });
            Each(cat =>
            {
                for (int i = 0; i < n; i++)
                {
                    cat.P[i] = Collide(cat, cat.P[i] + cat.ContactDelta[i]);
                    cat.V[i] += cat.ContactVelocity[i];
                }
            });
        }
    }
    void Behavior(Cat cat, float delta)
    {
        cat.BehaviorClock += delta;
        if (cat.BehaviorClock < 1 / BehaviorHz) return;
        float dt = cat.BehaviorClock; cat.BehaviorClock = 0;
        if (cat.Dragging) { cat.State = "drag"; return; }
        if (cat.ReactionLeft > 0) { cat.ReactionLeft -= dt; cat.State = "react"; return; }
        cat.DecisionLeft -= dt;
        if (cat.DecisionLeft <= 0)
        {
            bool walk = cat.State != "walk"; cat.State = walk ? "walk" : "idle";
            cat.DecisionLeft = (walk ? 1.2f : 2.5f) * (0.7f + (float)cat.Rng.NextDouble() * 0.6f);
            cat.Axes = walk ? new FastVector2(cat.Rng.Next(2) == 0 ? -0.15f : 0.15f, 0) : FastVector2.Zero;
        }
    }
    public override void _PhysicsProcess(double delta) => Simulate((float)delta, false);
    public override void _Process(double delta) { if (RenderEnabled) RenderNow(); }
    public void Advance(float delta) => Simulate(delta, true);
    void Simulate(float delta, bool render)
    {
        SyncCount();
        if (_lastWater != WaterMode) { foreach (var cat in _cats) cat.Water = WaterMode; _lastWater = WaterMode; }
        if (_selected >= 0)
        {
            var selected = _cats[_selected]; selected.DragTarget = (FastVector2)GetLocalMousePosition() + _dragOffset;
            if (KeyboardControl)
            {
                selected.Axes = new FastVector2((Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0),
                    (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0));
                selected.Gather = Input.IsPhysicalKeyPressed(Key.E);
            }
        }
        long start = Stopwatch.GetTimestamp();
        SolverMilliseconds = 0; ContactMilliseconds = 0; MergeMilliseconds = 0;
        if (SimulationEnabled)
        {
            foreach (var cat in _cats)
            {
                cat.Elapsed += delta;
                if (AutonomousBehavior && (!KeyboardControl || _selected < 0 || cat != _cats[_selected])) Behavior(cat, delta);
                if (cat.Jump)
                {
                    for (int i = 0; i < cat.P.Length; i++)
                    {
                        if (cat.Water) { float dx = cat.P[i].X - cat.Center.X; cat.V[i] += new FastVector2(dx * 1.5f, -115 * MathF.Exp(-dx * dx / 150)); }
                        else if (cat.Grounded) cat.V[i].Y -= 130;
                    }
                    cat.Jump = false;
                }
                cat.Grounded = false;
            }
            float dt = Math.Min(delta, 1f / 30) / Substeps;
            for (int substep = 0; substep < Substeps; substep++)
            {
                long solverStart = Stopwatch.GetTimestamp();
                Each(cat => Step(cat, dt));
                SolverMilliseconds += Stopwatch.GetElapsedTime(solverStart).TotalMilliseconds;
                long contactStart = Stopwatch.GetTimestamp();
                if (MergeOnContact)
                {
                    long mergeStart = Stopwatch.GetTimestamp(); MergeTouching();
                    MergeMilliseconds += Stopwatch.GetElapsedTime(mergeStart).TotalMilliseconds;
                }
                else if (InterCatContacts && _cats.Count > 1) Contacts();
                ContactMilliseconds += Stopwatch.GetElapsedTime(contactStart).TotalMilliseconds;
            }
        }
        Each(cat => cat.Center = Centroid(cat));
        PhysicsMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (RenderEnabled && render) RenderNow();
    }
    public void RenderNow()
    {
        long start = Stopwatch.GetTimestamp();
        Each(Rasterize);
        // All masks are complete before shared coverage is read by workers.
        if (!MergeOnContact) Array.Fill(_bodyOwner, -1);
        if (!MergeOnContact) foreach (var cat in _cats)
        {
            var r = cat.Raster;
            for (int y = 0; y < r.H; y++) for (int x = 0; x < r.W; x++)
                if (r.Mask[y * r.W + x] != 0)
                    _bodyOwner[(y + r.Y) * WorldSize.X + x + r.X] = cat.Id;
        }
        Each(PaintBody);
        RasterMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        foreach (var cat in _cats)
        {
            var r = cat.Raster;
            if (cat.Image == null) cat.Image = Image.CreateFromData(r.W, r.H, false, Image.Format.Rgba8, r.Bytes);
            else cat.Image.SetData(r.W, r.H, false, Image.Format.Rgba8, r.Bytes);
            if (cat.Texture == null) cat.Texture = ImageTexture.CreateFromImage(cat.Image);
            else if (cat.Texture.GetWidth() != r.W || cat.Texture.GetHeight() != r.H) cat.Texture.SetImage(cat.Image);
            else cat.Texture.Update(cat.Image);
            var f = cat.Features;
            if (cat.FeaturesImage == null) cat.FeaturesImage = Image.CreateFromData(f.W, f.H, false, Image.Format.Rgba8, f.Bytes);
            else cat.FeaturesImage.SetData(f.W, f.H, false, Image.Format.Rgba8, f.Bytes);
            if (cat.FeaturesTexture == null) cat.FeaturesTexture = ImageTexture.CreateFromImage(cat.FeaturesImage);
            else if (cat.FeaturesTexture.GetWidth() != f.W || cat.FeaturesTexture.GetHeight() != f.H) cat.FeaturesTexture.SetImage(cat.FeaturesImage);
            else cat.FeaturesTexture.Update(cat.FeaturesImage);
        }
        UploadMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        QueueRedraw();
    }

    void Rasterize(Cat cat)
    {
        var r = cat.Raster;
        int worldW = Math.Max(4, (int)MathF.Ceiling(WorldSize.X / cat.Scale)), worldH = Math.Max(4, (int)MathF.Ceiling(WorldSize.Y / cat.Scale));
        int x0 = worldW - 1, y0 = worldH - 1, x1 = 0, y1 = 0;
        int radius = cat.Water ? 12 : 9, extent = radius + 1;
        foreach (var worldP in cat.P)
        {
            var p = worldP / cat.Scale;
            x0 = Math.Min(x0, (int)p.X - extent); y0 = Math.Min(y0, (int)p.Y - extent);
            x1 = Math.Max(x1, (int)p.X + extent); y1 = Math.Max(y1, (int)p.Y + extent);
        }
        x0 = Math.Clamp(x0, 1, worldW - 2); y0 = Math.Clamp(y0, 1, worldH - 2);
        x1 = Math.Clamp(x1, 1, worldW - 2); y1 = Math.Clamp(y1, 1, worldH - 2);
        // Leave room for surface-grown ears, plus empty neighbor-query border.
        r.X = Math.Max(0, x0 - 2); r.Y = Math.Max(0, y0 - 15);
        int w = ((x1 - r.X + 3 + 15) / 16) * 16, h = ((y1 - r.Y + 3 + 15) / 16) * 16;
        r.Resize(w, h);
        Array.Clear(r.Field); Array.Clear(r.Mask); Array.Clear(r.Bytes);
        for (int i = 0; i < cat.P.Length; i++)
        {
            var p = cat.P[i] / cat.Scale; int ox = (int)p.X, oy = (int)p.Y;
            for (int y = Math.Max(0, oy - radius); y < Math.Min(worldH, oy + extent); y++)
                for (int x = Math.Max(0, ox - radius); x < Math.Min(worldW, ox + extent); x++)
                {
                    float dx = x - p.X, dy = y - p.Y, distance = (dx * dx + dy * dy) / (radius * radius);
                    if (distance >= 1) continue;
                    float contribution = (1 - distance) * (1 - distance);
                    int cell = (y - r.Y) * w + x - r.X;
                    r.Field[cell] += contribution;
                }
        }
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            int cell = (y - r.Y) * w + x - r.X;
            if (r.Field[cell] >= 0.58f && !Solid((int)(x * cat.Scale), (int)(y * cat.Scale))) r.Mask[cell] = 1;
        }
        if (cat.Water) FillWaterHoles(cat);
        SelectFace(cat);
    }
    void FillWaterHoles(Cat cat)
    {
        var r = cat.Raster;
        Array.Copy(r.Mask, r.Remaining, r.Mask.Length);
        // Mark empty regions once. Only small enclosed gaps are numerical holes;
        // exterior air, detached droplets and solid terrain remain untouched.
        for (int seed = 0; seed < r.Mask.Length; seed++)
        {
            if (r.Remaining[seed] != 0) continue;
            int count = 1, head = 0; bool exterior = false, solid = false;
            r.Queue[0] = seed; r.Remaining[seed] = 2;
            while (head < count)
            {
                int cell = r.Queue[head++], x = cell % r.W, y = cell / r.W;
                exterior |= x == 0 || y == 0 || x == r.W - 1 || y == r.H - 1;
                solid |= Solid((int)((x + r.X) * cat.Scale), (int)((y + r.Y) * cat.Scale));
                if (x > 0) Visit(cell - 1);
                if (x + 1 < r.W) Visit(cell + 1);
                if (y > 0) Visit(cell - r.W);
                if (y + 1 < r.H) Visit(cell + r.W);
            }
            if (!exterior && !solid && count <= 96)
                for (int i = 0; i < count; i++) r.Mask[r.Queue[i]] = 1;
            void Visit(int cell)
            {
                if (r.Remaining[cell] != 0) return;
                r.Remaining[cell] = 2; r.Queue[count++] = cell;
            }
        }
    }
    bool OtherBodyNear(Cat cat, int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
        {
            int px = x + dx, py = y + dy;
            if ((uint)px >= WorldSize.X || (uint)py >= WorldSize.Y) continue;
            int owner = _bodyOwner[py * WorldSize.X + px];
            if (owner >= 0 && owner != cat.Id) return true;
        }
        return false;
    }
    void PaintBody(Cat cat)
    {
        var r = cat.Raster; int w = r.W;
        int x0 = r.X + 1, y0 = r.Y + 1, x1 = Math.Min((int)MathF.Ceiling(WorldSize.X / cat.Scale) - 2, r.X + r.W - 2), y1 = Math.Min((int)MathF.Ceiling(WorldSize.Y / cat.Scale) - 2, r.Y + r.H - 2);
        Color fur = cat.Water ? cat.Fur.Lerp(WaterTint, 0.72f) : cat.Fur;
        Color shade = cat.Water ? WaterShade : cat.Shade;
        uint furPixel = Pack(fur), inkPixel = Pack(Ink), shadePixel = Pack(shade);
        uint highlightPixel = Pack(WaterHighlight), sparklePixel = Pack(WaterSparkle);
        var pixels = MemoryMarshal.Cast<byte, uint>(r.Bytes);
        int sparkleX = (int)(cat.Elapsed * 10), sparkleY = (int)(MathF.Sin(cat.Elapsed * 2) * 2);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
        {
            int cell = (y - r.Y) * w + x - r.X;
            if (r.Mask[cell] == 0) continue;
            uint color = furPixel;
            if (r.Mask[cell - 1] == 0 || r.Mask[cell + 1] == 0 || r.Mask[cell - w] == 0 || r.Mask[cell + w] == 0) color = inkPixel;
            else if (r.Mask[Math.Min(r.Mask.Length - 1, cell + w * 2)] == 0) color = shadePixel;
            else if (cat.Water && r.Mask[Math.Max(0, cell - w * 3)] == 0) color = highlightPixel;
            else if (cat.Water && PosMod(x + sparkleX, 29) < 5 && PosMod(y + sparkleY, 11) == 0) color = sparklePixel;
            if (!MergeOnContact && color != furPixel && OtherBodyNear(cat, x, y)) color = furPixel;
            pixels[cell] = color;
        }

        PaintFeatures(cat, fur);
    }
    static uint Pack(Color color)
    {
        uint r = (uint)Math.Clamp((int)MathF.Round(color.R * 255), 0, 255);
        uint g = (uint)Math.Clamp((int)MathF.Round(color.G * 255), 0, 255);
        uint b = (uint)Math.Clamp((int)MathF.Round(color.B * 255), 0, 255);
        uint a = (uint)Math.Clamp((int)MathF.Round(color.A * 255), 0, 255);
        return BitConverter.IsLittleEndian ? r | g << 8 | b << 16 | a << 24 : r << 24 | g << 16 | b << 8 | a;
    }
    static int PosMod(int value, int divisor) => (value % divisor + divisor) % divisor;
    static void Pixel(Raster r, int x, int y, Color color)
    {
        x -= r.X; y -= r.Y;
        if ((uint)x >= r.W || (uint)y >= r.H) return;
        int i = (y * r.W + x) * 4;
        r.Bytes[i] = (byte)Math.Clamp((int)MathF.Round(color.R * 255), 0, 255);
        r.Bytes[i + 1] = (byte)Math.Clamp((int)MathF.Round(color.G * 255), 0, 255);
        r.Bytes[i + 2] = (byte)Math.Clamp((int)MathF.Round(color.B * 255), 0, 255);
        r.Bytes[i + 3] = (byte)Math.Clamp((int)MathF.Round(color.A * 255), 0, 255);
    }
    static bool Covered(Raster r, int x, int y, int width, int height, int padding)
    {
        int left = x - width / 2 - padding, top = y - height / 2 - padding;
        int right = left + width + padding * 2, bottom = top + height + padding * 2;
        if (left < 1 || top < 1 || right >= r.W || bottom >= r.H) return false;
        int stride = r.W + 1;
        int filled = r.Sum[bottom * stride + right] - r.Sum[top * stride + right] - r.Sum[bottom * stride + left] + r.Sum[top * stride + left];
        return filled == (right - left) * (bottom - top);
    }
    static void SelectFace(Cat cat)
    {
        var r = cat.Raster; int w = r.W, h = r.H;
        var localCenter = cat.Center / cat.Scale;
        var tracking = cat.Face + localCenter - cat.LastCenter; cat.LastCenter = localCenter;
        Array.Clear(r.Sum); Array.Clear(r.Feature); Array.Copy(r.Mask, r.Remaining, r.Mask.Length);
        for (int y = 0; y < h; y++)
        {
            int row = 0;
            for (int x = 0; x < w; x++) { row += r.Mask[y * w + x]; r.Sum[(y + 1) * (w + 1) + x + 1] = r.Sum[y * (w + 1) + x + 1] + row; }
        }
        r.LargestCount = 0;
        int trackedCell = ((int)MathF.Round(tracking.Y) - r.Y) * w + (int)MathF.Round(tracking.X) - r.X;
        bool trackingComponent = false;
        for (int seed = 0; seed < r.Mask.Length; seed++)
        {
            if (r.Remaining[seed] == 0) continue;
            int count = 1, head = 0; r.Queue[0] = seed; r.Remaining[seed] = 0;
            while (head < count)
            {
                int cell = r.Queue[head++];
                Flood(r, cell - 1, ref count); Flood(r, cell + 1, ref count);
                Flood(r, cell - w, ref count); Flood(r, cell + w, ref count);
            }
            bool containsTracking = false;
            for (int i = 0; i < count; i++) if (r.Queue[i] == trackedCell) { containsTracking = true; break; }
            if (containsTracking || (!trackingComponent && count > r.LargestCount))
            { Array.Copy(r.Queue, r.Largest, count); r.LargestCount = count; trackingComponent = containsTracking; }
        }
        for (int i = 0; i < r.LargestCount; i++) r.Feature[r.Largest[i]] = 1;
        bool wasSupported = r.FaceSupported; float previousScale = r.FaceScale;
        r.FaceSupported = false;
        ReadOnlySpan<float> scales = [1.2f, 1f, 0.85f, 0.7f, 0.55f, 0.4f, 0.25f, 0.16f];
        foreach (float scale in scales)
        {
            if (wasSupported && scale > previousScale && cat.Elapsed < cat.FaceGrowthAt) continue;
            int fw = Math.Max(3, (int)MathF.Round(21 * scale)), fh = Math.Max(2, (int)MathF.Round(13 * scale));
            int padding = scale >= 0.4f ? 2 : 1; float best = float.PositiveInfinity; FastVector2 anchor = FastVector2.Zero;
            for (int i = 0; i < r.LargestCount; i++)
            {
                int cell = r.Largest[i], x = cell % w, y = cell / w;
                if (!Covered(r, x, y, fw, fh, padding)) continue;
                var candidate = new FastVector2(x + r.X, y + r.Y);
                float travel = candidate.DistanceSquaredTo(tracking);
                if (travel > 9) continue;
                // Grow only with spare room, so a one-pixel ripple cannot toggle size.
                if (wasSupported && scale > previousScale && !Covered(r, x, y, fw, fh, padding + 2)) continue;
                float score = candidate.DistanceSquaredTo(localCenter - new FastVector2(0, 3)) * 0.05f + travel * 0.95f;
                if (score < best) { best = score; anchor = candidate; }
            }
            if (float.IsPositiveInfinity(best)) continue;
            if (scale != previousScale) cat.FaceGrowthAt = cat.Elapsed + 0.35f;
            cat.FaceAnchor = anchor; cat.Face = tracking.Lerp(anchor, 0.35f); r.FaceW = fw; r.FaceH = fh; r.FaceScale = scale; r.FaceSupported = true; return;
        }
        // If no supported position is reachable, approach the nearest body pixel
        // without drawing a detached face or teleporting to a distant water fragment.
        FastVector2 target = tracking; float nearest = float.PositiveInfinity;
        for (int i = 0; i < r.LargestCount; i++)
        {
            int cell = r.Largest[i]; var p = new FastVector2(cell % w + r.X, cell / w + r.Y);
            float distance = p.DistanceSquaredTo(tracking);
            if (distance < nearest) { nearest = distance; target = p; }
        }
        cat.Face = tracking + (target - tracking).LimitLength(2);
    }
    static void Flood(Raster r, int cell, ref int count)
    {
        if ((uint)cell >= r.Remaining.Length || r.Remaining[cell] == 0) return;
        r.Remaining[cell] = 0; r.Queue[count++] = cell;
    }
    bool Solid(int x, int y) => x < 0 || y < 0 || x >= WorldSize.X || y >= WorldSize.Y || _solid[y * WorldSize.X + x] != 0;
    void PaintEar(Cat cat, int x, Color fur)
    {
        var r = cat.Raster; int top = (int)cat.FaceAnchor.Y;
        while (top > r.Y && r.Feature[(top - r.Y) * r.W + x - r.X] != 0) top--;
        float scale = Math.Max(r.FaceScale, 0.4f);
        var b = new FastVector2(x, top + 3 * scale); var tip = b + new FastVector2(-1, -12) * scale;
        FastVector2[] triangle = [b + new FastVector2(-6, 2) * scale, tip, b + new FastVector2(6, 2) * scale];
        for (int y = Math.Max(1, (int)MathF.Floor(tip.Y)); y < Math.Min((int)MathF.Ceiling(WorldSize.Y / cat.Scale) - 1, (int)MathF.Ceiling(b.Y + 3)); y++)
            for (int px = Math.Max(1, (int)MathF.Floor(b.X - 6 * scale)); px < Math.Min((int)MathF.Ceiling(WorldSize.X / cat.Scale) - 1, (int)MathF.Ceiling(b.X + 6 * scale + 1)); px++)
            {
                var p = new FastVector2(px, y); if (Solid((int)(px * cat.Scale), (int)(y * cat.Scale)) || !Inside(p, triangle)) continue;
                Color color = fur;
                float edge = Math.Min(Closest(p, triangle[0], tip).DistanceTo(p), Closest(p, tip, triangle[2]).DistanceTo(p));
                if (edge < 0.8f) color = Ink;
                else if (y > tip.Y + 4 * scale && y < b.Y && Math.Abs(px - x) < 2 * scale) color = cat.Water ? WaterSparkle : cat.Ear;
                Pixel(cat.Features, px, y, color);
            }
    }
    void PaintFeatures(Cat cat, Color fur)
    {
        var r = cat.Raster; var f = cat.Features;
        if (!r.FaceSupported) { f.ResizePixels(1, 1); Array.Clear(f.Bytes); return; }
        int leftEar = (int)MathF.Round(cat.FaceAnchor.X - r.FaceW * 0.45f);
        int rightEar = (int)MathF.Round(cat.FaceAnchor.X + r.FaceW * 0.45f);
        int top = (int)cat.FaceAnchor.Y;
        for (int ear = 0; ear < 2; ear++)
        {
            int x = ear == 0 ? leftEar : rightEar;
            int y = (int)cat.FaceAnchor.Y;
            while (y > r.Y && r.Feature[(y - r.Y) * r.W + x - r.X] != 0) y--;
            top = Math.Min(top, y - (int)MathF.Ceiling(12 * Math.Max(r.FaceScale, 0.4f)));
        }
        f.X = (int)cat.FaceAnchor.X - r.FaceW / 2 - 9;
        f.Y = top;
        // Quantize allocation size so one-pixel face motion can reuse buffers/textures.
        int fw = r.FaceW + 18, fh = (int)cat.FaceAnchor.Y + r.FaceH / 2 + 3 - top;
        f.ResizePixels((fw + 7) / 8 * 8, (fh + 7) / 8 * 8);
        Array.Clear(f.Bytes);
        PaintEar(cat, (int)MathF.Round(cat.FaceAnchor.X - r.FaceW * 0.45f), fur);
        PaintEar(cat, (int)MathF.Round(cat.FaceAnchor.X + r.FaceW * 0.45f), fur);
        int ox = (int)cat.FaceAnchor.X - r.FaceW / 2, oy = (int)cat.FaceAnchor.Y - r.FaceH / 2;
        for (int y = 0; y < r.FaceH; y++) for (int x = 0; x < r.FaceW; x++)
        {
            int sx = Math.Min(20, (int)((x + 0.5f) * 21 / r.FaceW)), sy = Math.Min(12, (int)((y + 0.5f) * 13 / r.FaceH));
            Color color = cat.FacePixels[sy * 21 + sx];
            if (cat.Elapsed % 4.4f > 4.24f && sy < 7)
            {
                color.A = 0;
                if (sy == 4 && (sx >= 3 && sx <= 5 || sx >= 15 && sx <= 17)) color = Ink;
            }
            if (color.A > 0)
            {
                Pixel(f, ox + x, oy + y, color);
            }
        }
    }
    public override void _Draw()
    {
        foreach (var cat in _cats)
        {
            if (cat.Texture == null) continue;
            DrawTextureRect(cat.Texture, new Rect2(new Godot.Vector2(cat.Raster.X, cat.Raster.Y) * cat.Scale, new Godot.Vector2(cat.Raster.W, cat.Raster.H) * cat.Scale), false);
        }
        foreach (var cat in _cats)
            if (cat.FeaturesTexture != null) DrawTextureRect(cat.FeaturesTexture, new Rect2(new Godot.Vector2(cat.Features.X, cat.Features.Y) * cat.Scale, new Godot.Vector2(cat.Features.W, cat.Features.H) * cat.Scale), false);
        if (DebugParticles) foreach (var cat in _cats) foreach (var p in cat.P) DrawCircle((Godot.Vector2)p, 1, new Color("de99a9"));
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
                foreach (var cat in _cats) { cat.Dragging = false; if (cat.State == "drag") cat.State = "idle"; }
            else if (mouse.Pressed && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                FastVector2 position = GetLocalMousePosition(); int hit = -1; float best = 12 * 12;
                for (int c = _cats.Count - 1; c >= 0; c--) foreach (var p in _cats[c].P)
                {
                    float distance = p.DistanceSquaredTo(position) / (_cats[c].Scale * _cats[c].Scale);
                    if (distance < best) { best = distance; hit = c; }
                }
                if (hit >= 0)
                {
                    if (_selected >= 0) { _cats[_selected].Axes = FastVector2.Zero; _cats[_selected].Gather = false; _cats[_selected].Dragging = false; }
                    _selected = hit;
                    if (mouse.ButtonIndex == MouseButton.Left)
                    {
                        _cats[hit].Dragging = true; _cats[hit].State = "drag"; _dragOffset = _cats[hit].Center - position;
                    }
                    else { _cats[hit].Jump = true; _cats[hit].ReactionLeft = 0.8f; _cats[hit].State = "react"; }
                    EmitSignal(SignalName.CatInteracted, _cats[hit].Id); GetViewport().SetInputAsHandled();
                }
            }
        }
        if (input is InputEventKey key && key.Pressed && !key.Echo && _selected >= 0)
        {
            if (key.PhysicalKeycode is Key.Space or Key.W or Key.Up) _cats[_selected].Jump = true;
            if (key.PhysicalKeycode == Key.V) DebugParticles = !DebugParticles;
            if (key.PhysicalKeycode == Key.F) _cats[_selected].Water = !_cats[_selected].Water;
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut) foreach (var cat in _cats) { cat.Dragging = false; cat.Axes = FastVector2.Zero; cat.Gather = false; }
    }
    public Godot.Collections.Dictionary Snapshot(int index)
    {
        var cat = _cats[index]; bool finite = true; int penetrating = 0;
        foreach (var p in cat.P)
        {
            finite &= p.IsFinite();
            foreach (var terrain in _terrain) if (Inside(p, terrain.Polygon)) { penetrating++; break; }
        }
        foreach (var v in cat.V) finite &= v.IsFinite();
        return new() { ["cat_id"] = cat.Id, ["points"] = Array.ConvertAll(cat.P, p => (Godot.Vector2)p), ["velocities"] = Array.ConvertAll(cat.V, p => (Godot.Vector2)p), ["center"] = (Godot.Vector2)cat.Center,
            ["particles"] = cat.P.Length, ["finite"] = finite, ["penetrating"] = penetrating,
            ["mass_units"] = cat.MassUnits, ["body_scale"] = cat.Scale, ["particle_mass"] = (float)cat.MassUnits / cat.P.Length,
            ["water"] = cat.Water, ["face_supported"] = cat.Raster.FaceSupported,
            ["face_anchor"] = (Godot.Vector2)(cat.FaceAnchor * cat.Scale), ["face_size"] = new Vector2I(cat.Raster.FaceW, cat.Raster.FaceH),
            ["internal_center_drift"] = cat.Drift, ["state"] = cat.State };
    }
    public Godot.Collections.Dictionary Metrics()
    {
        long bytes = 0; int mass = 0; foreach (var cat in _cats) mass += cat.MassUnits;
        foreach (var cat in _cats) bytes += cat.Raster.Bytes.Length + cat.Features.Bytes.Length;
        return new() { ["cats"] = _cats.Count, ["mass_units"] = mass, ["merge_ms"] = MergeMilliseconds, ["particles"] = ParticleCount, ["physics_ms"] = PhysicsMilliseconds,
            ["solver_ms"] = SolverMilliseconds, ["contacts_ms"] = ContactMilliseconds,
            ["workers"] = _parallel.MaxDegreeOfParallelism,
            ["raster_ms"] = RasterMilliseconds, ["upload_ms"] = UploadMilliseconds, ["texture_bytes"] = bytes };
    }
    public override void _ExitTree()
    {
        foreach (var cat in _cats) { cat.Texture?.Dispose(); cat.Image?.Dispose(); cat.FeaturesTexture?.Dispose(); cat.FeaturesImage?.Dispose(); }
    }
}
