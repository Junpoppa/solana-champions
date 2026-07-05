Shader "Custom/XRayFill"
{
    // Pass 2 of the see-through effect: solid silhouette fill. Draws only where the mesh is OCCLUDED
    // (_ZTest = Greater). Two-bit stencil (ReadMask 3): bit 2 = "bean visible here" (set by XRayMask,
    // skip self-occlusion in normal view); bit 1 = "fill already drawn here" (set by IncrSat so the
    // fill draws EXACTLY ONCE per pixel — stacked occluded limbs no longer blend into cyan bands).
    // Comp Equal vs Ref 0 over both bits => draw only where neither bit is set.
    // Renders just after the mask (queue Transparent+50). _ZTest=Always (8) draws everywhere (debug).
    //
    // Slope-scaled polygon Offset (toward camera) defeats Z-FIGHTING vs the real bean: the dup mesh
    // and the URP-Lit bean use different vertex shaders, so their depth isn't bit-identical. On grazing
    // thin surfaces (arms/legs/feet/edges) that tiny mismatch made ZTest Greater spuriously pass against
    // the bean's OWN near-coincident surface -> cyan leaked there. Nudging the fill nearer makes a
    // self-coincident surface read as in-front (Greater fails), while real occluders (clearly nearer)
    // still pass. Factor scales with slope, so the worst-leaking grazing limbs get the most bias.
    Properties
    {
        [HDR] _Color ("Fill Color", Color) = (0.30, 0.75, 1.0, 1.0)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 5   // 5=Greater, 8=Always
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2            // 2=Back
        _OffsetFactor ("Depth Offset Factor (slope)", Float) = -1                   // more negative = more bias on grazing polys
        _OffsetUnits  ("Depth Offset Units", Float) = -1                            // constant near-bias
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "XRayFill"
            Tags { "LightMode"="UniversalForward" }

            ZTest [_ZTest]
            ZWrite Off
            Cull [_Cull]
            Offset [_OffsetFactor], [_OffsetUnits]   // pull tested depth toward camera -> kills self z-fight
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil {
                Ref 0
                ReadMask 3       // inspect both bits: visible (2) + drawn (1)
                WriteMask 1      // only ever touch the drawn bit
                Comp Equal       // pass only when (stencil & 3) == 0 -> not visible AND not yet drawn
                Pass IncrSat     // mark drawn so deeper layers at this pixel are skipped (one flat layer)
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ZTest;
                float _Cull;
                float _OffsetFactor;
                float _OffsetUnits;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
    Fallback Off
}
