// The grass on top of the toon core (Grass.shader): fine painterly brush strokes up close and a warm grade.
// The strokes are world-space value noise stretched along the mowing direction (x) and leaning a little this way
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
    float big = GrassNoise(r * float2(0.33, 1.0));            // long strokes, three times longer than wide
    float small = GrassNoise(r * float2(0.66, 2.0) + 37.0);
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

half3 GrassGrade(half3 col)
{
    col = lerp(Luma(col).xxx, col, _GradeSaturation);
    return max(col * _GradeGain.rgb + _GradeLift.rgb, 0);
}

#endif
