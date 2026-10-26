// The field's shader (WS-11): the toon look (ToonCore.hlsl: the same cel split, cast and contact shadows, haze) with
// fine painterly brush strokes up close and a warm grade on top (GrassCore.hlsl). Used by View/FieldView.cs for the
// painted field and the sideline grass through Resources/Grass.mat; FEATURES.grassShader (NO_GRASS_SHADER=1) puts
// the plain toon shader back. Flat ground only: no outline, casts nothing.
Shader "PocketParty/Grass"
{
    Properties
    {
        _MainTex ("Paint", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _PlayerColor ("Player Colour (unused)", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade: value drop (near-grey, a touch cool)", Color) = (0.67, 0.67, 0.73, 1)
        _ShadeHue ("Shade family (one cool purple)", Color) = (0.62, 0.58, 0.84, 1)
        _ShadeUnify ("Shade: hue shift toward the family", Range(0, 1)) = 0.4
        _ShadeLift ("Shade lift (darks go navy)", Color) = (0.10, 0.10, 0.21, 1)
        _ShadeSaturation ("Shade: most saturation it keeps", Range(0, 1)) = 0.72
        _LitColor ("Lit (warm tint)", Color) = (1.0, 0.96, 0.86, 1)
        _Threshold ("Light Threshold", Range(-1, 1)) = 0.08
        _Specular ("Specular", Range(0, 1)) = 0
        _SpecSize ("Specular Dot Size", Range(0.001, 0.2)) = 0.016
        _Rim ("Rim Light (stepped)", Range(0, 1)) = 0
        _RimWidth ("Rim width (1 - N.V)", Range(0, 1)) = 0.15
        _BounceColor ("Cool bounce on shaded undersides", Color) = (0.30, 0.46, 0.78, 1)
        _RimColor ("Rim Colour (warm)", Color) = (1.0, 0.93, 0.80, 1)
        _OutlineColor ("Outline Hue (unused)", Color) = (0.114, 0.169, 0.290, 1)
        _OutlineTint ("Outline: share of surface colour (unused)", Range(0, 1)) = 0.75
        _OutlineDark ("Outline: darkness (unused)", Range(0, 0.3)) = 0.045
        _FogAmount ("Haze amount", Range(0, 1)) = 1
        _OutlineWidth ("Outline Width (unused)", Float) = 0
        _OutlineMinPx ("Outline min (unused)", Float) = 0
        _OutlineInner ("Outline inner (unused)", Range(0, 1)) = 0.4
        _OutlineRefDist ("Outline ref dist (unused)", Float) = 16
        _OutlineMinScale ("Outline min scale (unused)", Range(0, 1)) = 0.5
        _OutlineZBias ("Outline z bias (unused)", Float) = 0.06
        _DropShadow ("Receives the far drop shadows (0/1)", Float) = 0
        _DetailM ("Brush stroke width (m)", Float) = 0.11
        _DetailAmount ("Brush stroke strength", Range(0, 0.5)) = 0.09
        _DetailFadePx ("Strokes fade out under (px)", Float) = 3
        _GradeSaturation ("Grade: saturation", Range(0, 1.5)) = 0.86
        _GradeGain ("Grade: gain (linear)", Vector) = (1.1, 1.07, 1.0, 1)
        _GradeLift ("Grade: warm lift (linear)", Vector) = (0.02, 0.016, 0.012, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "GrassCore.hlsl"
        ENDHLSL

        Pass
        {
            Name "Grass"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half4 color : COLOR; float2 uv : TEXCOORD1; float fog : TEXCOORD2; float3 positionWS : TEXCOORD3; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half3 albedo = GrassDetail(ToonAlbedo(i.color, i.uv), i.positionWS);
                half3 col = GrassGrade(ToonShade(albedo, i.positionWS, i.normalWS, i.color.a));
                return half4(lerp(col, MixFog(col, i.fog), _FogAmount), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert (float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
