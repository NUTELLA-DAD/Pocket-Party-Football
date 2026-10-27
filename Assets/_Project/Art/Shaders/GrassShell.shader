// The grass blades (FEATURES.grassBlades): stacked shells over the painted field (View/FieldView.cs builds them, one
// mesh, uv2.x = each shell's height share). Each shell keeps only the pixels inside a blade (GrassCore.hlsl
// GrassBlade) and shades them like the field under them (the same paint, strokes, toon light, shadows and grade),
// darker at the root and lighter at the tip in flat steps. Static: no wind, no time (no wobble). Drawn after the
// field (queue Geometry-1, the field Geometry-2) without writing depth, so anything laid on the grass later (the
// scrimmage and first-down lines, rings, shadows) lies on top of the blades. Casts nothing.
Shader "PocketParty/GrassShell"
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
        _DetailScale ("Stroke noise frequency (x, z)", Vector) = (0.33, 1.0, 0, 0)
        _BladeHeightM ("Blade height (m)", Float) = 0.22
        _BladeSpacingM ("One blade per (m)", Float) = 0.2
        _BladeWidth ("Blade half-width (share of a cell)", Float) = 0.55
        _BladeFlat ("Blade flatness", Float) = 2
        _BladeLeanM ("Tip curl (m)", Float) = 0.08
        _BladeSteps ("Light steps root to tip", Float) = 2
        _BladeRootDark ("Root darker by", Range(0, 1)) = 0.15
        _BladeTipLight ("Tip lighter by", Range(0, 1)) = 0.22
        _PaintRect ("Painted field rect (min x, min z, 1/size x, 1/size z)", Vector) = (0, 0, 1, 1)
        _GradeSaturation ("Grade: saturation", Range(0, 1.5)) = 0.86
        _GradeGain ("Grade: gain (linear)", Vector) = (1.1, 1.07, 1.0, 1)
        _GradeLift ("Grade: warm lift (linear)", Vector) = (0.02, 0.016, 0.012, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-1" }

        HLSLINCLUDE
        #include "GrassCore.hlsl"
        ENDHLSL

        Pass
        {
            Name "GrassShell"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half4 color : COLOR; float2 uv : TEXCOORD1; float fog : TEXCOORD2; float3 positionWS : TEXCOORD3; float shell : TEXCOORD4; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                o.shell = v.uv2.x;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float tip;
                float2 root;
                if (!GrassBlade(i.positionWS.xz, i.shell, tip, root)) discard;
                // the paint where the blade stands, so a blade is all line or all grass (no fringe along the markings);
                // the top mip, since the uv is constant across a blade and its derivatives say nothing
                half4 tex = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, saturate((root - _PaintRect.xy) * _PaintRect.zw), 0);
                half3 albedo = GrassDetail(lerp(_PlayerColor.rgb, tex.rgb, tex.a) * i.color.rgb * _BaseColor.rgb, i.positionWS);
                albedo *= GrassBladeShade(albedo, tip);
                half3 col = GrassGrade(ToonShade(albedo, i.positionWS, i.normalWS, i.color.a));
                return half4(lerp(col, MixFog(col, i.fog), _FogAmount), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
