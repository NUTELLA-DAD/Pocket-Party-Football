// The sunset sky box (FEATURES.sunsetSky, View/SkyView.cs feeds every colour from game.json "sky"): a deep blue-violet
// zenith down through mauve and pink to a hot orange horizon on the sun's side (pinker and duskier away from it), a big
// low sun with a soft glow, long painted cloud streaks lit orange-pink underneath with a gold edge toward the sun, and a
// dusky line of hills along the horizon. Below the horizon it is the haze colour, the same as the fog, so the far
// ground melts into it with no seam. Clouds are cut with hard toon edges; nothing moves (no time, no wobble).
Shader "PocketParty/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.16, 0.18, 0.42, 1)
        _High ("High sky", Color) = (0.38, 0.32, 0.62, 1)
        _Low ("Low sky", Color) = (0.93, 0.55, 0.62, 1)
        _Horizon ("Horizon (away from the sun)", Color) = (0.98, 0.72, 0.62, 1)
        _SunLow ("Low sky toward the sun", Color) = (1.0, 0.6, 0.35, 1)
        _SunHorizon ("Horizon toward the sun", Color) = (1.0, 0.82, 0.45, 1)
        _Disc ("Sun disc", Color) = (1.0, 0.93, 0.7, 1)
        _Haze ("Haze (below the horizon, = fog)", Color) = (0.55, 0.45, 0.55, 1)
        _Hill ("Hills", Color) = (0.42, 0.33, 0.48, 1)
        _CloudLit ("Cloud lit", Color) = (1.0, 0.62, 0.55, 1)
        _CloudShade ("Cloud shade", Color) = (0.55, 0.42, 0.66, 1)
        _CloudRim ("Cloud rim", Color) = (1.0, 0.86, 0.55, 1)
        _SunDir ("Painted sun direction", Vector) = (0.5, 0.15, 0.85, 0)
        _SunSizeDeg ("Sun size (deg)", Float) = 6
        _SunGlow ("Sun glow", Range(0, 3)) = 1
        _SunSpread ("Warm side spread", Range(0.5, 8)) = 2.5
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.42
        _CloudScale ("Cloud scale", Float) = 1.6
        _CloudStretch ("Cloud stretch", Float) = 3.5
        _CloudHeight ("Cloud layer curve", Float) = 0.12
        _HillsDeg ("Hills height (deg)", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Pass
        {
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _High, _Low, _Horizon, _SunLow, _SunHorizon, _Disc, _Haze, _Hill;
                half4 _CloudLit, _CloudShade, _CloudRim;
                float4 _SunDir;
                float _SunSizeDeg, _SunGlow, _SunSpread;
                float _CloudCover, _CloudScale, _CloudStretch, _CloudHeight, _HillsDeg;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 dir = normalize(v.positionOS.xyz);
                float4 cs = mul(UNITY_MATRIX_VP, float4(_WorldSpaceCameraPos + dir * 100.0, 1.0));
                #if UNITY_REVERSED_Z
                    cs.z = cs.w * 1e-6;
                #else
                    cs.z = cs.w * (1 - 1e-6);
                #endif
                o.positionCS = cs;
                o.dir = dir;
                return o;
            }

            // value noise and a few octaves of it (fixed, seeded by position only)
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += Noise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }
            // the cloud layer's own coordinates: turned a little, streaked long
            float2 CloudUV(float2 p) { float2 q = float2(p.x * 0.8 + p.y * 0.6, -p.x * 0.6 + p.y * 0.8); return float2(q.x / _CloudStretch, q.y); }
            float Noise1(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(Hash(float2(i, 3.1)), Hash(float2(i + 1, 3.1)), f); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sd = normalize(_SunDir.xyz);
                float2 flatD = normalize(d.xz + 1e-5), flatS = normalize(sd.xz + 1e-5);
                float toSun = pow(saturate(dot(flatD, flatS) * 0.5 + 0.5), _SunSpread);   // 1 toward the sun's bearing
                float elev = asin(clamp(d.y, -1, 1)) * 57.2958;                             // degrees above the horizon

                // the hills along the horizon, and the haze below it
                float az = atan2(d.x, d.z);
                float hills = _HillsDeg * (0.45 + 0.55 * Noise1(az * 7.0) + 0.25 * Noise1(az * 23.0 + 4.0));

                // the gradient: horizon -> low -> high -> zenith, the low part warmer toward the sun
                half3 hor = lerp(_Horizon.rgb, _SunHorizon.rgb, toSun);
                half3 low = lerp(_Low.rgb, _SunLow.rgb, toSun);
                float e = elev / 90.0;
                half3 col = lerp(hor, low, smoothstep(0.0, 0.07, e));
                col = lerp(col, _High.rgb, smoothstep(0.06, 0.32, e));
                col = lerp(col, _Zenith.rgb, smoothstep(0.3, 0.85, e));

                // the sun: a glow round it, then the disc with a crisp edge
                float cosA = dot(d, sd);
                float ang = acos(clamp(cosA, -1, 1)) * 57.2958;
                float r = _SunSizeDeg * 0.5;
                col += _SunHorizon.rgb * _SunGlow * (0.55 * exp(-ang / (r * 3.0)) + 0.25 * exp(-ang / (r * 12.0)));
                float aa = max(fwidth(ang), 1e-3);
                col = lerp(col, _Disc.rgb * 1.25, saturate((r - ang) / aa + 0.5));

                // the clouds: a layer seen from below, streaked along one direction, cut with a hard edge
                float2 p = d.xz / (d.y + _CloudHeight) * _CloudScale;
                float n = Fbm(CloudUV(p));
                float thr = 0.75 - _CloudCover * 0.5;
                float edge = max(fwidth(n), 1e-3);
                float cloud = saturate((n - thr) / edge + 0.5);
                // fade them out at the horizon and high up, and keep the sun clear
                cloud *= smoothstep(0.015, 0.06, e) * (1 - smoothstep(0.55, 0.8, e)) * smoothstep(r * 1.4, r * 2.6, ang);
                // lit underneath toward the sun, a gold edge on the sun side
                float nSun = Fbm(CloudUV(p + flatS * 0.08));
                float rim = saturate((n - nSun) * 9.0) * step(thr, n);
                half3 cc = lerp(_CloudShade.rgb, _CloudLit.rgb, saturate(toSun * 1.2 + (1 - e * 2) * 0.35));
                // a second, deeper tone in the thick middle of each cloud (two flat toon tones, like the field's stripes)
                cc = lerp(cc, lerp(cc, _CloudShade.rgb, 0.45), saturate((n - thr - 0.07) / edge + 0.5));
                cc = lerp(cc, _CloudRim.rgb, step(0.25, rim) * (0.35 + 0.65 * toSun));
                col = lerp(col, cc, cloud);
                // (chosen last, not returned early: the fwidth()s above need every pixel to run them)
                half3 hillCol = lerp(_Hill.rgb, _Haze.rgb, 0.25 * (1 - saturate(elev / max(hills, 1e-3))));
                col = elev < hills ? hillCol : col;
                col = elev < 0 ? _Haze.rgb : col;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
