// The Wind Waker-style sky dome (LOOK.md L0): a clean vertical gradient from the pale haze at the horizon (the fog
// colour, so the far sea melts into it) up to the deep sky blue (the camera's background colour, which the
// Kraken's dusk recolours: SkyView copies both every frame), plus the sun: a small hot disc and a warm halo
// (bright enough for the bloom). Drawn at the far plane around the camera; no time-based animation.
Shader "PocketParty/Sky"
{
    Properties
    {
        _HorizonBand ("Horizon band height", Range(0.01, 1)) = 0.32
        _SunSize ("Sun disc size", Range(0.0001, 0.02)) = 0.0005
        _SunGlow ("Sun halo", Range(0, 2)) = 0.25
        _HorizonGlow ("Warm horizon glow toward the sun", Range(0, 1)) = 0.22
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _HorizonBand;
                half _SunSize;
                half _SunGlow;
                half _HorizonGlow;
            CBUFFER_END
            half4 _SkyTop, _SkyHorizon;   // set by SkyView (linear)
            float4 _SkySunDir;            // the painted sun (SkyView: up front-left by day, the real low sun at dusk)
            float _SkyDusk;               // 0 day .. 1 dusk (SkyView, from the sun's colour)

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

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half h = saturate(d.y / _HorizonBand);
                // a soft, slightly eased band: pale at the horizon, the full blue from a third of the way up
                half t = 1 - (1 - h) * (1 - h);
                half3 col = lerp(_SkyHorizon.rgb, _SkyTop.rgb, t);
                // the drawn sun: painted up front-left where the camera can see it (the light itself comes from behind
                // the camera's left shoulder so the faces we look at stay lit), its halo and a warm glow along the horizon
                Light sun = GetMainLight();
                float3 sd3 = normalize(_SkySunDir.xyz + float3(0, 1e-4, 0));
                float sd = dot(d, sd3);
                half disc = saturate((sd - (1 - _SunSize)) / max(fwidth(sd), 1e-5) + 0.5);
                // a corona: a bright ring close round the disc falling off into a wide soft glow
                half halo = pow(saturate(sd), 120) * _SunGlow + pow(saturate(sd), 12) * _SunGlow * 0.15;
                float2 flatD = normalize(d.xz + 1e-5), flatS = normalize(sd3.xz + 1e-5);
                // (faded in just above the horizon, so the sky meets the fogged sea in the very same colour: no seam)
                half band = exp(-max(d.y, 0) * 9) * smoothstep(0.0, 0.08, d.y) * (0.3 + 0.7 * pow(saturate(dot(flatD, flatS) * 0.5 + 0.5), 4));
                half3 warmSun = sun.color * half3(1.0, 0.86, 0.62);
                col += warmSun * band * _HorizonGlow + sun.color * halo;
                // dusk (WW): a warm orange glow all round the horizon under a deep violet-blue zenith
                half ring = exp(-max(d.y, 0) * 5) * smoothstep(0.0, 0.05, d.y);
                col = lerp(col, lerp(col, half3(1.0, 0.45, 0.16), 0.75), ring * _SkyDusk);
                col = lerp(col, sun.color * half3(1.3, 1.25, 1.08), disc);   // (a clean pale-gold disc; the bloom adds a small glow)
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
