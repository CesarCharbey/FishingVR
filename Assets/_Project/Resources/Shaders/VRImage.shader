// Flat image (render texture of a UI canvas) for VR, URP, single-pass instanced ready.
// Used by CanvasSnapshot instead of UI/Default, which only draws in the left eye on this project.
// Same structure as LakeWater (which draws in both eyes): URP Core.hlsl, instancing / stereo macros,
// SRP Batcher compatible. The texture is premultiplied (UI drawn over a transparent black clear).
// Kept in a Resources folder so that Shader.Find finds it in builds.
Shader "FishingVR/VRImage"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Image", 2D) = "white" {}
        _BaseColor ("Tint (alpha = fade)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VRImage"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                return c * _BaseColor.a * half4(_BaseColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
