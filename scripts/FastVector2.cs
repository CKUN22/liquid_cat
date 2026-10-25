using System;
using System.Runtime.CompilerServices;

// Keep hot math in the optimized game assembly. Godot's debug C# math assembly
// otherwise introduces millions of non-inlined calls in this workload.
internal struct FastVector2
{
    public float X, Y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public FastVector2(float x, float y) { X = x; Y = y; }
    public static FastVector2 Zero => default;
    public static FastVector2 Up => new(0, -1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly float LengthSquared() => X * X + Y * Y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly float Length() => MathF.Sqrt(LengthSquared());
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly float Dot(FastVector2 b) => X * b.X + Y * b.Y;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly float DistanceSquaredTo(FastVector2 b) { float x = X - b.X, y = Y - b.Y; return x * x + y * y; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly float DistanceTo(FastVector2 b) => MathF.Sqrt(DistanceSquaredTo(b));
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly FastVector2 Normalized() { float length = Length(); return length > 0 ? this / length : Zero; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly FastVector2 LimitLength(float limit) { float length = Length(); return length > limit ? this / length * limit : this; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly FastVector2 Lerp(FastVector2 b, float t) => new(X + (b.X - X) * t, Y + (b.Y - Y) * t);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly FastVector2 Min(FastVector2 b) => new(Math.Min(X, b.X), Math.Min(Y, b.Y));
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly FastVector2 Max(FastVector2 b) => new(Math.Max(X, b.X), Math.Max(Y, b.Y));
    public readonly FastVector2 Round() => new(MathF.Round(X), MathF.Round(Y));
    public readonly bool IsFinite() => float.IsFinite(X) && float.IsFinite(Y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static FastVector2 operator +(FastVector2 a, FastVector2 b) => new(a.X + b.X, a.Y + b.Y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static FastVector2 operator -(FastVector2 a, FastVector2 b) => new(a.X - b.X, a.Y - b.Y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static FastVector2 operator *(FastVector2 a, float b) => new(a.X * b, a.Y * b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static FastVector2 operator /(FastVector2 a, float b) => new(a.X / b, a.Y / b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator FastVector2(Godot.Vector2 v) => new(v.X, v.Y);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static implicit operator Godot.Vector2(FastVector2 v) => new(v.X, v.Y);
}
