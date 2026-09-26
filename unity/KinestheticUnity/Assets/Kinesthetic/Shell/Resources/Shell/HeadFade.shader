// The view's own curtain: a quad on the live camera that fades the whole view to one colour, or narrows
// it to a tunnel. Unlit, drawn after everything and over everything, and stereo-aware, because it exists
// for the headset first. Two dials: _Cover is a flat fade, _Tunnel darkens the edges inward.
Shader "Kinesthetic/HeadFade"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
        _Cover ("Cover", Range(0, 1)) = 0
        _Tunnel ("Tunnel", Range(0, 1)) = 0
        _Extent ("Extent", Float) = 0.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "HeadFade"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Cover;
            half _Tunnel;
            half _Extent;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // 0 at the centre of the view, 1 at the edge of what the camera can see: the quad itself is
                // far larger than any view, and HeadFade sets _Extent to the fraction of it the view reaches.
                float r = length(input.uv - 0.5) * 2 / max(_Extent, 0.01);
                float inner = lerp(1.0, 0.12, _Tunnel);
                half tunnel = _Tunnel > 0 ? smoothstep(inner, inner + 0.3, r) : 0;
                return half4(_Color.rgb, max(_Cover, tunnel));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
