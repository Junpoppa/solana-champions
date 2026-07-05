Shader "Custom/XRayMask"
{
    // Pass 1 of the see-through effect: marks the bean's VISIBLE (front-most) footprint into stencil
    // BIT 2 (Ref 2, WriteMask 2). Writes no color/depth. Renders just before the fill (queue Transparent+49).
    // The fill skips anywhere bit 2 is set, which kills self-occlusion (arm/leg behind own body in normal view).
    // Bit 1 is reserved for the fill's own "drawn once" flag (see XRayFill).
    //
    // Slope-scaled polygon Offset (toward camera) is CRITICAL here: the mask dup and the URP-Lit real bean
    // use different vertex shaders, so their depth isn't bit-identical. At grazing angles (body shell over a
    // buried arm-root / leg-top) the mask's ZTest LEqual lost that z-fight and FAILED to mark the body, so
    // the buried limb behind it leaked cyan through the fill. The near-bias makes LEqual pass reliably so the
    // body footprint is always marked. Bias is small vs the bean-occluder gap, so it never marks the bean as
    // "visible" when it's actually behind the beam/drum (X-ray preserved).
    Properties
    {
        _OffsetFactor ("Depth Offset Factor (slope)", Float) = -1   // more negative = more bias on grazing polys
        _OffsetUnits  ("Depth Offset Units", Float) = -1            // constant near-bias
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+49" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "XRayMask"
            Tags { "LightMode"="UniversalForward" }

            ZTest LEqual   // only where the bean is the front-most surface (its visible silhouette)
            ZWrite Off
            ColorMask 0
            Cull Back
            Offset [_OffsetFactor], [_OffsetUnits]   // bias mask NEARER so grazing body footprint marks reliably
            Stencil { Ref 2 Comp Always Pass Replace WriteMask 2 }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                float _OffsetFactor;
                float _OffsetUnits;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
