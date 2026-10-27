// Soft see-through marks drawn over the field: the throw arc's ribbon and the FIND ME glow under a player's feet.
// Unlit; the vertex colour is the colour and its alpha how solid it is, so a mesh fades smoothly from a solid middle
// to nothing at its edges (no hard blocks). No depth writes, so it never cuts into the grass or the players.
Shader "PocketParty/Glow"
{
    Properties
    {
        _FogAmount ("Fog amount", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _FogAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float fog : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half3 col = lerp(i.color.rgb, MixFog(i.color.rgb, i.fog), _FogAmount);
                return half4(col, i.color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
