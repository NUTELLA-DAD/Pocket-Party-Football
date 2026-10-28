// The shared Wind Waker-style toon core (LOOK.md L0) for Poster.shader (every solid mesh) and Ghost.shader (the
// same look with a dollhouse cut-away: a clean round, ink-rimmed hole round each crew member it hides from the camera).
// Vertex contract (all game meshes):
//   COLOR.rgb = albedo (linear); COLOR.a = flags (never transparency):
//     1.0 = normal (ink outline, matte)   0.75 = shiny (outline + a hard spec dot: brass, noses, buttons, metal)
//     0.5 = shiny, no outline (lenses, highlights, glowing pupils)   0.25 or 0 = painted decal: matte, no outline,
//     casts no shadow (eyes, markings, painted strokes)
//   NORMAL = the shading normal (smooth for round shapes).
//   TANGENT.xyz with w = 1 = the outline direction (MeshKit writes the averaged normal); w = 0: use the normal.
//   TEXCOORD0 = the palette texture's uv (Kenney models; its alpha 0 = the player colour).
// Globals fed every frame by LookFeeder (Boot) through View/Cutaway.cs and View/DropShadows.cs: the cut-away holes,
// the crew's feet (contact shadows) and the big casters' drop shadows on the world far below.
// No time-based animation (no wobble).
#ifndef POCKETPARTY_TOON_CORE
#define POCKETPARTY_TOON_CORE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4 _BaseColor;
    half4 _PlayerColor;
    half4 _ShadeColor;
    half4 _ShadeHue;
    half _ShadeUnify;
    half4 _ShadeLift;
    half _ShadeSaturation;
    half4 _LitColor;
    half _Threshold;
    half _Specular;
    half _SpecSize;
    half _Rim;
    half _RimWidth;
    half4 _BounceColor;
    half4 _RimColor;
    half4 _OutlineColor;
    half _OutlineTint;
    half _OutlineDark;
    half _FogAmount;
    float _OutlineWidth;
    float _OutlineMinPx;
    float _OutlineRefDist;
    half _OutlineMinScale;
    half _OutlineInner;
    float _OutlineZBias;
    half _DropShadow;
    // FEATURES.jerseyNumbers / FEATURES.eyesLook (View/JerseyMarks.cs): the players' own numbers and moving eyes
    half _Marks;
    float4 _NumberDigits;   // the tens (or only) digit, the ones digit, how many digits (0: no number)
    half4 _NumberFill;
    half4 _NumberEdge;
    float4 _NumberStyle;    // pen half-width, edge width, gap between the digits (glyph units)
    float4 _EyeLook;        // the iris's offset (iris radii: x to his left, y up), the lid's close (0..1), mood (+1 happy, -1 wince)
    half4 _IrisColor;
    half4 _LidColor;
    // FEATURES.grassStains (View/Reactions.cs): how much grass he has on him, the stains' scale and opacity, their colours
    float4 _Stain;
    float4 _StainLook;
    half4 _StainGrass;
    half4 _StainDirt;
    // FEATURES.skinTones: this man's skin as a ratio of the model's own (1, 1, 1: as painted)
    float4 _Skin;
CBUFFER_END
TEXTURE2D(_MarksTex); SAMPLER(sampler_MarksTex);
TEXTURE2D(_DigitsTex); SAMPLER(sampler_DigitsTex);

// ---- the marks map (tools/rig/marks.py): where a number or an eye goes on the model's colour map ----
//   R, G = the texel's spot in its zone (number: -1.5..1.5 number heights; eye: -3..3 iris radii from the iris at rest),
//   B = 0.5 number / 1 eye, A = inside the zone (an eye: inside its opening).
// The digits (JerseyMarks.Digits): ten cells, each the glyph box (1 x 1.4) with 0.4 round it, the distance to the
// glyph's centre line in R (0..0.6 glyph units).
float MarkDigit(float digit, float gx, float gy)
{
    if (gx < -0.35 || gx > 1.35 || gy < -0.35 || gy > 1.75) return 9;
    float2 at = float2((digit + (gx + 0.4) / 1.8) / 10.0, (gy + 0.4) / 2.2);
    return SAMPLE_TEXTURE2D_LOD(_DigitsTex, sampler_DigitsTex, at, 0).r * 0.6;
}

half3 MarkNumber(half3 col, float2 q, float aa)
{
    float n = _NumberDigits.z;
    if (n < 0.5) return col;
    const float K = 1.0 / 1.4;   // a glyph's width in number heights
    float gap = _NumberStyle.z;
    float x = q.x + (n * K + (n - 1) * gap) * 0.5;
    float gy = (q.y + 0.5) / K;
    float d = MarkDigit(_NumberDigits.x, x / K, gy);
    if (n > 1.5) d = min(d, MarkDigit(_NumberDigits.y, (x - K - gap) / K, gy));
    float a = aa / K, pen = _NumberStyle.x;
    col = lerp(col, _NumberEdge.rgb, saturate((pen + _NumberStyle.y - d) / a + 0.5));
    return lerp(col, _NumberFill.rgb, saturate((pen - d) / a + 0.5));
}

half3 MarkEye(half3 col, float2 e, float aa)
{
    float2 p = e - _EyeLook.xy;
    float r = length(p);
    // the iris: darker under the lid, lighter low down, a dark rim; the pupil; two catch-lights (fixed, like painted ones)
    half3 iris = _IrisColor.rgb * lerp(1.25, 0.55, saturate(p.y * 0.5 + 0.5));
    iris = lerp(iris, _IrisColor.rgb * 0.35, saturate((r - 0.82) / aa + 0.5));
    iris = lerp(iris, _IrisColor.rgb * 0.08, saturate((0.46 - r) / aa + 0.5));
    iris = lerp(iris, half3(1, 1, 1), saturate((0.22 - length(p - float2(-0.32, 0.36))) / aa + 0.5));
    iris = lerp(iris, half3(1, 1, 1), saturate((0.09 - length(p - float2(0.3, -0.36))) / aa + 0.5));
    col = lerp(col, iris, saturate((1.0 - r) / aa + 0.5));
    // a blink: the lid comes down from the top of the opening, a dark lash line along its edge
    float lid = 2.0 - _EyeLook.z * 4.2;
    col = lerp(col, _IrisColor.rgb * 0.15, step(0.01, _EyeLook.z) * saturate((e.y - (lid - 0.22)) / aa + 0.5));
    col = lerp(col, _LidColor.rgb * _Skin.rgb, saturate((e.y - lid) / aa + 0.5));
    // a reaction (FEATURES.faceReactions): the eye shut, drawn as one dark line on the lid: happy, an upturned arc (^);
    // a wince, a shallow V pressed shut
    float mood = _EyeLook.w;
    if (abs(mood) > 0.5)
    {
        float lidLine = mood > 0 ? 0.35 - 0.55 * e.x * e.x : -0.1 + 0.35 * abs(e.x);
        col = lerp(_LidColor.rgb * _Skin.rgb, _IrisColor.rgb * 0.12, saturate((0.2 - abs(e.y - lidLine)) / aa + 0.5));
    }
    return col;
}

// FEATURES.grassStains: blotches of grass and dirt where the marks map allows them (outside every number and eye zone,
// its R: legs and knees most, the helmet and hands not at all), more of them the more he has been down
float StainHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}
float StainNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(StainHash(i), StainHash(i + float2(1, 0)), f.x), lerp(StainHash(i + float2(0, 1)), StainHash(i + float2(1, 1)), f.x), f.y);
}
half3 MarkStain(half3 col, float2 uv, float n, float aa, float allow)
{
    float sc = _StainLook.x;
    float cover = _Stain.x * allow;
    // a soft edge (a smear, not a cut-out); grass green or dirt brown by patch, laid over the cloth's own shading
    float k = saturate((cover * 0.8 - n) / max(aa, 0.07) + 0.5) * step(0.01, cover);
    k *= 0.55 + 0.45 * StainNoise(uv * sc * 5.0 + 9.0);   // (rubbed in, not painted flat)
    half3 tint = lerp(_StainGrass.rgb, _StainDirt.rgb, saturate((StainNoise(uv * sc * 0.4 + 5.0) - 0.5) * 6.0 + 0.5));
    half3 over = tint * lerp(0.75, 1.15, dot(col, half3(0.2126, 0.7152, 0.0722)));
    return lerp(col, over, k * _StainLook.y);
}

half3 Marks(half3 col, float2 uv)
{
    half3 outCol = col;
    UNITY_BRANCH if (_Marks > 0.5)
    {
        half4 m = SAMPLE_TEXTURE2D(_MarksTex, sampler_MarksTex, uv);
        float2 q = (m.rg - 0.5) * 3.0;
        float aa = clamp(fwidth(q.x) + fwidth(q.y), 1e-3, 0.05);   // (worked out before any branch on the texel)
        float sn = StainNoise(uv * _StainLook.x) * 0.65 + StainNoise(uv * _StainLook.x * 2.3 + 17.0) * 0.35;
        float saa = max(fwidth(sn), 1e-3);
        // skin: the marks map's G outside the zones, and all of an eye zone round its opening (the lids and brows)
        float skinW = (m.b < 0.25 ? m.g : (m.b > 0.75 ? 1.0 : 0.0)) * saturate(dot(col, half3(0.2126, 0.7152, 0.0722)) * 12.0 - 0.15);   // (not the dark brows and lashes)
        half3 skin = col * lerp(half3(1, 1, 1), (half3)_Skin.rgb, skinW);
        if (m.a >= 0.5) outCol = m.b < 0.75 ? MarkNumber(col, q, aa) : MarkEye(col, q * 2.0, aa * 2.0);
        else outCol = MarkStain(skin, uv, sn, saa, m.r * step(m.b, 0.25));
    }
    return outCol;
}

half3 ToonAlbedo(half4 vcol, float2 uv)
{
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    return lerp(_PlayerColor.rgb, Marks(tex.rgb, uv), tex.a) * vcol.rgb * _BaseColor.rgb;
}
half FlagShiny(half a) { return (a > 0.375 && a < 0.875) ? 1 : 0; }
half FlagOutline(half a) { return a > 0.625 ? 1 : 0; }
half FlagCasts(half a) { return a > 0.375 ? 1 : 0; }
half Luma(half3 c) { return dot(c, half3(0.2126, 0.7152, 0.0722)); }

half3 ToonShadeColour(half3 albedo)
{
    // one rule for every surface, material-aware: a value drop (_ShadeColor), then a hue shift toward one fixed cool
    // purple (_ShadeHue) that is strong for neutrals and cool colours but slight for saturated warm ones (wood, skin,
    // orange keep their family and go darker and a touch redder: brown wood shades to red-brown, not mauve); then the
    // saturation is clamped (warm keeps more), so no shade turns electric; darks are lifted a little toward navy
    half amx = max(albedo.r, max(albedo.g, albedo.b)), amn = min(albedo.r, min(albedo.g, albedo.b));
    half asat = (amx - amn) / max(amx, 1e-4);
    half warm = saturate((albedo.r - albedo.b) / max(amx, 1e-4) * 1.6) * saturate(asat * 2.2);   // 1 = saturated warm
    half3 c = albedo * _ShadeColor.rgb * lerp(half3(1, 1, 1), half3(0.98, 0.84, 0.86), warm);
    half3 hue = _ShadeHue.rgb / max(Luma(_ShadeHue.rgb), 1e-3);
    c = lerp(c, Luma(c) * hue, _ShadeUnify * lerp(1.0, 0.2, warm));
    half mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
    half s = (mx - mn) / max(mx, 1e-4);
    c = lerp(Luma(c).xxx, c, min(1, lerp(_ShadeSaturation, 0.95, warm) / max(s, 1e-4)));
    // (an ambient floor: deep shade, never black, even in cast shadow on dark wood; dark colours get the full navy lift)
    half darkA = saturate(1 - Luma(albedo) * 5);
    c = max(c, albedo * 0.45);
    return max(c, 0) + _ShadeLift.rgb * lerp(lerp(1.0, 0.4, warm), 1.3, darkA);
}

// ---- the dollhouse cut-away (Ghost.shader): Cutaway.cs uploads the holes every frame ----
#define CUT_MAX 32
float4 _CutHoles[CUT_MAX];   // xy = viewport position (0..1, bottom-left), z = radius (share of the viewport height), w = its view depth (m)
float _CutHoleCount;
float _CutAspect;

// keep = 1 outside every hole (0 inside, anti-aliased over a pixel); rim = the ink ring just outside a hole's edge.
// anyDepth: cut whatever the depth (the outline hull's far side would otherwise show through)
void CutawayEval(float4 positionNDC, float viewDepth, bool anyDepth, out float keep, out float rim)
{
    keep = 1; rim = 0;
    float2 uv = positionNDC.xy / max(positionNDC.w, 1e-5);
    int count = (int)_CutHoleCount;
    const float rimPx = 2.5;
    for (int i = 0; i < CUT_MAX; i++)
    {
        if (i >= count) break;
        float4 h = _CutHoles[i];
        if (!anyDepth && viewDepth > h.w - 0.4) continue;   // only what is in front of them
        float dpx = length((uv - h.xy) * float2(_CutAspect, 1)) * _ScreenParams.y;
        float rpx = h.z * _ScreenParams.y;
        float outside = saturate(dpx - rpx + 0.5);
        keep = min(keep, outside);
        rim = max(rim, outside * saturate(rpx + rimPx - dpx + 0.5));
    }
}

// ---- the dollhouse deck cut-away (Ghost.shader): whatever Ghost surface stands in front of a deck's volume (the
// space above a ship's main deck where the crew and the stations are, or the hold while someone is down there) is
// cut away along a crisp, inked edge, so the stations show; the balloon's top and silhouette stay (their view lines
// pass over the deck). Cutaway.cs uploads the boxes every frame. ----
#define DECK_MAX 8
float4 _DeckBoxA[DECK_MAX];   // xyz = the box's centre (world), w = its yaw (radians, about +y)
float4 _DeckBoxB[DECK_MAX];   // xyz = its half sizes, w = the cut height (world y): only what is below it is cut
float _DeckBoxCount;

// how far (metres along the view line) the line from the camera through this point, beyond it, runs inside a deck box:
// > 0 cut, < 0 by how much it misses (the ink rim)
// tMin: 0 = only beyond this point (a surface in front of the deck); -1e4 = anywhere on the camera's line (the outline)
float DeckCutDepth(float3 positionWS, float tMin)
{
    float best = -1e5;
    float3 dir = normalize(positionWS - _WorldSpaceCameraPos);
    int count = (int)_DeckBoxCount;
    for (int i = 0; i < DECK_MAX; i++)
    {
        if (i >= count) break;
        float4 a = _DeckBoxA[i], b = _DeckBoxB[i];
        float cs = cos(a.w), sn = sin(a.w);
        float3 p = positionWS - a.xyz;
        p = float3(cs * p.x - sn * p.z, p.y, sn * p.x + cs * p.z);
        float3 d = float3(cs * dir.x - sn * dir.z, dir.y, sn * dir.x + cs * dir.z);
        float3 inv = 1.0 / (abs(d) > 1e-5 ? d : 1e-5);
        float3 t0 = (-b.xyz - p) * inv, t1 = (b.xyz - p) * inv;
        float3 tn = min(t0, t1), tf = max(t0, t1);
        float tEnter = max(max(tn.x, tn.y), max(tn.z, tMin));
        float tExit = min(min(tf.x, tf.y), tf.z);
        // (and only below the box's cut height: the envelope's upper half and its silhouette stay)
        best = max(best, min(tExit - tEnter, b.w - positionWS.y));
    }
    return best;
}

// ---- contact shadows: a small dark disc on the ground round each crew member's feet ----
#define FEET_MAX 32
float4 _Feet[FEET_MAX];   // xyz = the feet (world), w = the disc's radius (m)
float _FeetCount;

half ContactShadow(float3 positionWS)
{
    half s = 0;
    float pixelM = max(length(fwidth(positionWS)), 1e-4);   // (one pixel in metres, outside the loop: no divergent derivatives)
    int count = (int)_FeetCount;
    for (int i = 0; i < FEET_MAX; i++)
    {
        if (i >= count) break;
        float4 f = _Feet[i];
        float dy = positionWS.y - f.y;
        if (dy < -0.12 || dy > 0.12) continue;
        float2 d = (positionWS.xz - f.xz) / f.w;
        float r = length(float2(d.x, d.y * 1.15));
        s = max(s, saturate((1 - r) * f.w / pixelM + 0.5));
    }
    return s;
}

#include "DropShadow.hlsl"

// ---- the lit colour ----
half3 ToonShade(half3 albedo, float3 positionWS, float3 normalWS, half flags)
{
    float3 n = normalize(normalWS);
    float3 vdir = normalize(GetWorldSpaceViewDir(positionWS));
    float viewDist = distance(positionWS, _WorldSpaceCameraPos);
    // (the shadow is looked up a little out along the normal, more far away where the shadow map's texels are bigger:
    // no self-shadow acne, no checkered stair-steps on thin ropes and spars)
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS + n * (0.05 + viewDist * 0.0018)));

    // the cel split: a crisp terminator (anti-aliased over one pixel) and the hard cast shadow
    float ndl = dot(n, sun.direction);
    float aa = max(fwidth(ndl), 0.012);   // (at least a sliver of N.L: a clean continuous line even on modest tessellation)
    half lit = saturate((ndl - _Threshold) / aa + 0.5);
    // (the soft-filtered shadow thresholded at one pixel: a crisp edge with smooth, round contours, no stairs)
    half atten = sun.shadowAttenuation;
    half shadow = saturate((atten - 0.5) / max(fwidth(atten), 0.03) + 0.5);
    // far from the camera the shadow map's texels get big: let the filtered edge show there instead of its stair-steps
    shadow = lerp(shadow, smoothstep(0.15, 0.85, atten), saturate((viewDist - 45) / 60));
    shadow *= 1 - ContactShadow(positionWS);
    if (_DropShadow > 0.5) shadow *= 1 - DropShadow(positionWS, DropDir(sun.direction));
    lit *= shadow;

    // lit: warm (darks lifted to a readable navy-grey); shade: the one cool family, plus a cool bounce from the sea below
    // on shaded faces that look down (a curved underside gets a ramp, not a flat fill)
    half dark = saturate(1 - Luma(albedo) * 6);
    half3 sunCol = lerp(half3(1, 1, 1), sun.color, 0.6);   // (the dusk warms it, but never drowns the colours)
    half3 warm = albedo * _LitColor.rgb * sunCol + _ShadeLift.rgb * 2.4 * dark;
    half3 shadeCol = ToonShadeColour(albedo);
    half down = saturate(-n.y);
    shadeCol += _BounceColor.rgb * (Luma(albedo) + 0.15) * down * down;
    half3 col = lerp(shadeCol, warm, lit);

    // a crisp, shaped highlight (shiny vertices or a shiny material): an oval stroke stretched across the view, hard-edged
    half spec = max(_Specular, FlagShiny(flags));
    float3 h = normalize(sun.direction + vdir);
    float3 dv = mul((float3x3)UNITY_MATRIX_V, n - h);       // how far the normal is from the half vector, on screen
    float sd = length(float2(dv.x * 0.55, dv.y * 1.25)) / max(_SpecSize * 9, 1e-3);
    float saa = max(fwidth(sd), 1e-3);
    half dot1 = saturate((1 - sd) / saa + 0.5) * saturate(lit * 2) * spec;
    col = lerp(col, lerp(warm, half3(1, 1, 1), 0.75) * 1.2, dot1);

    // a stepped (cel) rim: a hard bright band along the silhouette on the sun's side, in light or in shade, so figures,
    // the envelope and the hull separate from what is behind them
    float ndv = saturate(dot(n, vdir));
    float rimAa = max(fwidth(ndv), 1e-3);
    float3 sunFlat = normalize(float3(sun.direction.x, 0, sun.direction.z) + 1e-4);
    float sideDot = dot(n, sunFlat) - 0.1;
    half side = saturate(sideDot / max(fwidth(sideDot), 1e-3) + 0.5);
    // (a narrow band just inside the ink, only on the sun-facing side and only in sunlight: a lighter version of the
    // local colour, never cream, so it never reads as a pale outline)
    half rim = saturate((_RimWidth - ndv) / rimAa + 0.5) * side * lit;
    half3 rimCol = min(warm * 1.22 + 0.04, max(warm, 1)) * lerp(half3(1, 1, 1), _RimColor.rgb, 0.3);
    col = lerp(col, rimCol, rim * _Rim);
    return col;
}

// ---- the ink outline (an inverted hull) ----
struct OutlineAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; half4 color : COLOR; float2 uv : TEXCOORD0; };
struct OutlineVaryings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; float4 positionNDC : TEXCOORD2; float depth : TEXCOORD3; float3 positionWS : TEXCOORD4; };

OutlineVaryings OutlineVert(OutlineAttributes v)
{
    OutlineVaryings o;
    float3 dirOS = v.tangentOS.w != 0 ? v.tangentOS.xyz : v.normalOS;
    float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 away = normalize(posWS - _WorldSpaceCameraPos);
    float3 dirWS = normalize(TransformObjectToWorldDir(dirOS));
    float facing = abs(dot(dirWS, away));
    // tucked a few cm behind, so coplanar neighbours (deck planks, tiles) hide each other's hull: lines only where
    // something stands out against something farther away (silhouettes), not along every seam
    float4 pos = TransformWorldToHClip(posWS + away * _OutlineZBias);
    float2 dirCS = mul((float3x3)UNITY_MATRIX_VP, dirWS).xy;
    float len = length(dirCS);
    // about even on screen: full width up close, tapering with distance but never under _OutlineMinPx at the
    // silhouette; thinner where the hull faces the camera (inner creases)
    float scale = clamp(_OutlineRefDist / max(pos.w, 0.01), _OutlineMinScale, 1.0);
    float minPx = lerp(_OutlineMinPx, _OutlineMinPx * 0.65, saturate((pos.w - 35) / 80));   // (lighter far away)
    float px = max(_OutlineWidth * scale, minPx) * lerp(1.0, _OutlineInner, facing) * _ScreenParams.y / 1080.0 * FlagOutline(v.color.a);
    if (len > 1e-5 && px > 0)
        pos.xy += dirCS / len * px * 2.0 / _ScreenParams.xy * pos.w;
    else if (px <= 0)
        pos = float4(0, 0, -2, 1);   // no outline here: a clipped vertex
    o.positionCS = pos;
    o.color = v.color;
    o.uv = v.uv;
    o.fog = ComputeFogFactor(pos.z);
    float4 ndc = pos * 0.5;
    o.positionNDC = float4(float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w, pos.zw);
    o.depth = pos.w;
    o.positionWS = posWS;
    return o;
}

// (never darker than the navy ink itself: a dark iron tower keeps a navy line, not a black slab)
// the ink: a dark, saturated version of the local colour (squared, so it keeps its hue and gains saturation), held
// under a fixed darkness, mixed with the navy ink hue; always dark, never a pale fringe
half3 InkColour(half3 albedo)
{
    half3 local = albedo * albedo;
    local *= min(1, _OutlineDark / max(Luma(local), 1e-4));
    return lerp(_OutlineColor.rgb, local, _OutlineTint);
}

half4 OutlineFrag(OutlineVaryings i)
{
    half3 albedo = ToonAlbedo(i.color, i.uv);
    half3 ink = InkColour(albedo);
    return half4(lerp(ink, MixFog(ink, i.fog), _FogAmount), 1);
}

// ---- the shadow caster (decals cast nothing) ----
float3 _LightDirection;
struct CasterAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
float4 CasterVert(CasterAttributes v) : SV_POSITION
{
    if (FlagCasts(v.color.a) <= 0) return float4(0, 0, -2, 1);
    float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 nWS = TransformObjectToWorldNormal(v.normalOS);
    float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
    #if UNITY_REVERSED_Z
        cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
    #else
        cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
    #endif
    return cs;
}

#endif
