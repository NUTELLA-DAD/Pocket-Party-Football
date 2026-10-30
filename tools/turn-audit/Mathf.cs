// A stand-in for the few UnityEngine.Mathf calls the view's pure maths makes, so the turn audit runs without Unity.
using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = (float)Math.PI, Deg2Rad = PI / 180f, Rad2Deg = 180f / PI, Epsilon = float.Epsilon;
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Acos(float f) => (float)Math.Acos(f);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int i) => Math.Abs(i);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Round(float f) => (float)Math.Round(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Repeat(float t, float len) => Clamp(t - (float)Math.Floor(t / len) * len, 0f, len);
        public static float DeltaAngle(float a, float b) { float d = Repeat(b - a, 360f); if (d > 180f) d -= 360f; return d; }
        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * Clamp01(t);
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = t * t * (3f - 2f * t); return a + (b - a) * t; }
    }
}
