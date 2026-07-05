Shader "Custom/CliffTwoTone"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0.85,0.94,0.64,1)
        _SideColor("Side Color", Color) = (0.87,0.72,0.76,1)
        _Threshold("Threshold", Range(0,1)) = 0.45
        _Blend("Blend", Range(0.001,0.6)) = 0.18
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 normalWS:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor; float4 _SideColor; float _Threshold; float _Blend;
            CBUFFER_END
            Varyings vert(Attributes IN){ Varyings OUT; float3 wp=TransformObjectToWorld(IN.positionOS.xyz); OUT.positionHCS=TransformWorldToHClip(wp); OUT.normalWS=TransformObjectToWorldNormal(IN.normalOS); return OUT; }
            half4 frag(Varyings IN):SV_Target{
                float3 n=normalize(IN.normalWS);
                float up=smoothstep(_Threshold-_Blend,_Threshold+_Blend,n.y);
                half3 base=lerp(_SideColor.rgb,_TopColor.rgb,up);
                Light L=GetMainLight();
                half ndl=saturate(dot(n,L.direction))*0.55+0.45;
                half3 col=base*L.color*ndl;
                return half4(col,1);
            }
            ENDHLSL
        }
    }
}