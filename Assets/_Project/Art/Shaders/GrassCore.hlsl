// The grass on top of the toon core (Grass.shader): fine painterly brush strokes up close and a warm grade.
// The strokes are world-space value noise stretched along one axis (_DetailScale; long along z reads round through
// the TV camera, which squashes z) and leaning a little this way
// and that, two sizes; they fade out where one stroke would be under _DetailFadePx pixels (no shimmer far away).
// Painted lines, numbers and the logo (bright paint) take them at a lower share, so the paint looks worn into the
// grass. Static: nothing here moves with time (no wobble).
#ifndef POCKETPARTY_GRASS_CORE
#define POCKETPARTY_GRASS_CORE

#include "ToonCore.hlsl"

// (outside the toon core's per-material buffer, so this material is not SRP-batched: two draws, the field and its surround)
float _DetailM;
half _DetailAmount;
float _DetailFadePx;
float4 _DetailScale;
// the blades (GrassShell.shader, FEATURES.grassBlades)
float _BladeHeightM, _BladeSpacingM, _BladeWidth, _BladeFlat, _BladeLeanM, _BladeSteps;
half _BladeRootDark, _BladeTipLight, _GroundShade;
float4 _PaintRect;   // the painted field's world rect: xy = its min corner (x, z), zw = 1 / its size

half _GradeSaturation;
half4 _GradeGain;
half4 _GradeLift;

float GrassHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float GrassNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    float a = GrassHash(i), b = GrassHash(i + float2(1, 0)), c = GrassHash(i + float2(0, 1)), d = GrassHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// -1..1: the brush strokes at this spot on the ground (xz in metres)
float GrassStrokes(float2 xz)
{
    float2 q = xz / max(_DetailM, 1e-3);
    // the lean wanders slowly over the field: strokes in one patch roughly agree, the next patch leans the other way
    float lean = (GrassNoise(q * 0.07 + 11.0) - 0.5) * 0.9;
    float cs = cos(lean), sn = sin(lean);
    float2 r = float2(cs * q.x - sn * q.y, sn * q.x + cs * q.y);
    // (_DetailScale: noise frequency along x and z; the lower one is the stroke's long axis)
    float big = GrassNoise(r * _DetailScale.xy);
    float small = GrassNoise(r * _DetailScale.xy * 2.0 + 37.0);
    return (big * 0.65 + small * 0.35) * 2 - 1;
}

half3 GrassDetail(half3 albedo, float3 positionWS)
{
    // one pixel in metres on the ground (the larger axis: the far side of the field is foreshortened)
    float2 px = fwidth(positionWS.xz);
    float pixelM = max(max(px.x, px.y), 1e-5);
    half fade = saturate((_DetailM / pixelM - _DetailFadePx) / max(_DetailFadePx, 1e-3));
    half paint = saturate((Luma(albedo) - 0.45) * 3);         // bright paint (linear luma) keeps less of it
    half d = GrassStrokes(positionWS.xz) * _DetailAmount * fade * lerp(1.0, 0.4, paint);
    // a lighter stroke is a little warmer (yellow), a darker one a little cooler
    return max(albedo * (1 + d * half3(1.2, 1.0, 0.6)), 0);
}

// Is there a blade at this spot (xz in metres) at height share t (0 the ground, 1 the tallest tip)? Each cell of
// _BladeSpacingM holds one seeded blade: its own height, a flat lens-shaped cross-section (_BladeFlat times longer
// than thick) turned its own way, tapering to a point and curling over a little (_BladeLeanM at the tip). The
// neighbouring cells are checked too, so blades overlap into tufts. tip: 0 at the root .. 1 at the tip; root: where
// the blade stands (xz, m), so a whole blade takes one colour of paint.
bool GrassBlade(float2 xz, float t, out float tip, out float2 root)
{
    float2 q = xz / max(_BladeSpacingM, 1e-3);
    float2 c = floor(q);
    bool hit = false;
    tip = 0;
    root = xz;
    [unroll] for (int j = -1; j <= 1; j++)
    [unroll] for (int i = -1; i <= 1; i++)
    {
        float2 cell = c + float2(i, j);
        float h1 = GrassHash(cell), h2 = GrassHash(cell + 17.3), h3 = GrassHash(cell + 41.7), h4 = GrassHash(cell + 73.1);
        float height = 0.55 + 0.45 * h3;
        float s = saturate(t / height);
        float a = h4 * 6.2832, ca = cos(a), sa = sin(a);
        float2 base = cell + 0.15 + 0.7 * float2(h1, h2);
        float2 d = q - (base + float2(ca, sa) * (_BladeLeanM / max(_BladeSpacingM, 1e-3)) * s * s);
        float u = d.x * ca + d.y * sa, v = (d.y * ca - d.x * sa) * _BladeFlat;
        float r = _BladeWidth * (1 - s);
        if (t <= height && u * u + v * v < r * r && s >= tip) { hit = true; tip = s; root = base * _BladeSpacingM; }
    }
    return hit;
}

// A blade's light in flat toon steps (_BladeSteps): darker at the root, lighter at the tip. Bright paint (lines,
// numbers) keeps most of its own colour, so the markings stay crisp.
half GrassBladeShade(half3 albedo, float tip)
{
    half tone = floor(tip * _BladeSteps + 0.5) / max(_BladeSteps, 1);
    half shade = lerp(1 - _BladeRootDark, 1 + _BladeTipLight, tone);
    half paint = saturate((Luma(albedo) - 0.45) * 3);
    return lerp(shade, 1, paint * 0.8);
}

half3 GrassGrade(half3 col)
{
    col = lerp(Luma(col).xxx, col, _GradeSaturation);
    return max(col * _GradeGain.rgb + _GradeLift.rgb, 0);
}

#endif
