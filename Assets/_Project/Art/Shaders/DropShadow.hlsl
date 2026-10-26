// Drop shadows of the big things (ships, balloons, the pylon, sky islands) on the world far below (LOOK.md L0):
// ellipsoid casters uploaded every frame by View/DropShadows.cs. Shared by ToonCore.hlsl and Sea.shader.
#ifndef AIRSHIP_DROP_SHADOW
#define AIRSHIP_DROP_SHADOW
#define DROP_MAX 32
float4 _DropA[DROP_MAX];   // xyz = an ellipsoid caster's centre (world), w = its yaw (radians, about +y)
float4 _DropB[DROP_MAX];   // xyz = its radii
float _DropCount;
float4 _DropToSun;        // the drop shadows' own light direction (toward the light; see DropShadows.cs); zero: the sun's

float3 DropDir(float3 sunDir) { return dot(_DropToSun.xyz, _DropToSun.xyz) > 0.5 ? _DropToSun.xyz : sunDir; }

// 1 where the line from this point toward the sun passes through a caster (hard, anti-aliased edge)
half DropShadow(float3 positionWS, float3 toSun)
{
    half s = 0;
    float pixelM = max(length(fwidth(positionWS)), 1e-4);   // (one pixel in metres, outside the loop: no divergent derivatives)
    int count = (int)_DropCount;
    for (int i = 0; i < DROP_MAX; i++)
    {
        if (i >= count) break;
        float4 a = _DropA[i], b = _DropB[i];
        float cs = cos(a.w), sn = sin(a.w);
        float3 p = positionWS - a.xyz, l = toSun;
        p = float3(cs * p.x - sn * p.z, p.y, sn * p.x + cs * p.z) / b.xyz;
        l = float3(cs * l.x - sn * l.z, l.y, sn * l.x + cs * l.z) / b.xyz;
        float ll = dot(l, l), pl = dot(p, l);
        if (pl >= 0) continue;                                 // the caster is not toward the sun
        float d = sqrt(max(dot(p, p) - pl * pl / ll, 0));      // the ray's closest approach (1 = the surface)
        s = max(s, saturate((1 - d) * min(b.x, b.z) / pixelM + 0.5));
    }
    return s;
}

#endif
