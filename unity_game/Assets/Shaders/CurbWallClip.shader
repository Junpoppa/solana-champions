Shader "Custom/CurbWallClip"
{
    // URP unlit + world-space clip plane. Fragments on the origin-curb side of the plane are
    // discarded so the wall looks like it emerges from the curb rail.
    // _ClipPoint / _ClipNormal are set per-wall via MaterialPropertyBlock (world space).
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.8, 0.1, 0.1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            // set via MaterialPropertyBlock (breaks SRP batching, fine for a few walls)
            float4 _ClipPoint;
            float4 _ClipNormal;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // keep fragments on the +normal (road/destination) side of the plane
                clip(dot(IN.positionWS - _ClipPoint.xyz, _ClipNormal.xyz));
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
