// The toon shader for every solid mesh (from Airship Crew, Kenny's own project): code-built figures, the stadium and the field.
// Crisp two-tone cel split from the sun, a blue-violet shade (never grey), a warm lit side, the sun's cast shadows
// thresholded into hard cel shadows, a hard spec dot and a warm rim, and ink outlines (an inverted hull) coloured
// from the surface, about even on screen. The vertex contract (flags in the vertex alpha) is in ToonCore.hlsl.
Shader "PocketParty/Toon"
{
    Properties
    {
        _MainTex ("Palette", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _PlayerColor ("Player Colour", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade: value drop (near-grey, a touch cool)", Color) = (0.67, 0.67, 0.73, 1)
        _ShadeHue ("Shade family (one cool purple)", Color) = (0.62, 0.58, 0.84, 1)
        _ShadeUnify ("Shade: hue shift toward the family", Range(0, 1)) = 0.4
        _ShadeLift ("Shade lift (darks go navy)", Color) = (0.10, 0.10, 0.21, 1)
        _ShadeSaturation ("Shade: most saturation it keeps", Range(0, 1)) = 0.72
        _LitColor ("Lit (warm tint)", Color) = (1.0, 0.96, 0.86, 1)
        _Threshold ("Light Threshold", Range(-1, 1)) = 0.08
        _Specular ("Specular (all vertices)", Range(0, 1)) = 0
        _SpecSize ("Specular Dot Size", Range(0.001, 0.2)) = 0.016
        _Rim ("Rim Light (stepped)", Range(0, 1)) = 0.8
        _RimWidth ("Rim width (1 - N.V)", Range(0, 1)) = 0.15
        _BounceColor ("Cool bounce on shaded undersides", Color) = (0.30, 0.46, 0.78, 1)
        _RimColor ("Rim Colour (warm)", Color) = (1.0, 0.93, 0.80, 1)
        _OutlineColor ("Outline Hue", Color) = (0.114, 0.169, 0.290, 1)
        _OutlineTint ("Outline: share of surface colour", Range(0, 1)) = 0.75
        _OutlineDark ("Outline: darkness of the local ink (linear luma)", Range(0, 0.3)) = 0.045
        _FogAmount ("Haze amount", Range(0, 1)) = 1
        _OutlineWidth ("Outline Width (px at 1080p, silhouette, near)", Float) = 3.0
        _OutlineMinPx ("Outline: thinnest silhouette (px at 1080p)", Float) = 2.0
        _OutlineInner ("Outline: inner-line share", Range(0, 1)) = 0.4
        _OutlineRefDist ("Outline full width within (m)", Float) = 16
        _OutlineMinScale ("Outline min width share (far)", Range(0, 1)) = 0.5
        _OutlineZBias ("Outline: tucked behind by (m)", Float) = 0.06
        _DropShadow ("Receives the far drop shadows (0/1)", Float) = 0
        [Header(Jersey numbers and eyes (View JerseyMarks))]
        _Marks ("Draws the marks map (0/1)", Float) = 0
        _MarksTex ("Marks map (linear)", 2D) = "black" {}
        _DigitsTex ("Digits (distance)", 2D) = "white" {}
        _NumberDigits ("Number: tens, ones, digits", Vector) = (0, 0, 0, 0)
        _NumberFill ("Number fill", Color) = (1, 1, 1, 1)
        _NumberEdge ("Number edge", Color) = (1, 0.75, 0.1, 1)
        _NumberStyle ("Number: pen, edge, gap", Vector) = (0.125, 0.08, 0.1, 0)
        _EyeLook ("Eyes: look x, y, blink", Vector) = (0, 0, 0, 0)
        _IrisColor ("Iris", Color) = (0.4, 0.22, 0.1, 1)
        _LidColor ("Eyelid", Color) = (0.6, 0.38, 0.22, 1)
        _Stain ("Grass on him (0..1)", Vector) = (0, 0, 0, 0)
        _StainLook ("Stains: scale, opacity", Vector) = (34, 0.85, 0, 0)
        _StainGrass ("Stain grass", Color) = (0.33, 0.48, 0.17, 1)
        _StainDirt ("Stain dirt", Color) = (0.43, 0.32, 0.2, 1)
        _Skin ("Skin tone (ratio to the model's)", Vector) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "ToonCore.hlsl"
        ENDHLSL

        Pass
        {
            Name "Toon"
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
                half3 col = ToonShade(ToonAlbedo(i.color, i.uv), i.positionWS, i.normalWS, i.color.a);
                return half4(lerp(col, MixFog(col, i.fog), _FogAmount), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment frag
            #pragma multi_compile_fog
            half4 frag (OutlineVaryings i) : SV_Target { return OutlineFrag(i); }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex CasterVert
            #pragma fragment frag
            half4 frag () : SV_Target { return 0; }
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
