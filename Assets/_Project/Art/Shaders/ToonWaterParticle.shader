// Cartoon water particles (foam rings, splash blobs, droplets) for URP, VR single-pass instanced ready.
//
// The texture alpha is a distance field (1 in the middle of the shape, 0 outside). The particle's vertex alpha
// does not fade the shape, it erodes it: crisp white shapes that grow, thin out and break up as they die
// (vertex alpha 1 = full shape, 0 = gone). Colour = texture RGB x vertex colour x tint, always opaque edges.
Shader "FishingVR/ToonWaterParticle"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Shape (alpha = distance field)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Erode ("Erosion Range", Range(0.05, 1)) = 0.85
        _LightInfluence ("Sun and Sky Influence", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "ToonWaterParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Erode;
                half _LightInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color       : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color       : COLOR;
                float2 uv         : TEXCOORD0;
                float fogFactor   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // Vertex alpha 1 -> keep everything above the outline, 0 -> nothing left.
                half edge = lerp(1.0, 0.5 - 0.5 * _Erode, saturate(input.color.a)) ;
                edge = max(edge, 0.02);
                half w = max(fwidth(tex.a) * 0.75, 1e-3);
                half shape = smoothstep(edge - w, edge + w, tex.a);
                clip(shape - 0.01);

                half3 ambient = SampleSH(half3(0, 1, 0));
                Light mainLight = GetMainLight();
                half3 light = lerp(half3(1, 1, 1), ambient + mainLight.color * saturate(mainLight.direction.y), _LightInfluence);
                half3 color = tex.rgb * input.color.rgb * _BaseColor.rgb * light;
                color = MixFog(color, input.fogFactor);
                return half4(color, shape * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
