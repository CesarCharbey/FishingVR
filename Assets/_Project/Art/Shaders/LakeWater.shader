// Calm lake surface for URP (VR single-pass instanced ready).
//
// Purely visual: the physics keeps using FlatWaterSurface (flat plane), so the surface is never displaced,
// only its normal is perturbed (animated ripple maps + interactive ripples from WaterRipples).
//
// What it does, per pixel:
// 1. Underwater: the scene behind the water (_CameraOpaqueTexture), slightly refracted, fades into the deep
//    colour with the distance travelled in the water (Beer-Lambert absorption). "Visibility" = distance at
//    which ~95% of the light is gone: fish shapes are readable near the surface, the bottom of the lake is not.
// 2. Surface: sky reflection with a Fresnel term (reflective at grazing angles, transparent when looking
//    down) and the sun's specular highlight, shadowed by the boat.
// 3. Foam: soft shore/contact foam (where the water is shallow: banks, dock posts, hull) and the wake foam
//    written by the interactive ripples.
//
// Requirements: URP asset with Depth Texture and Opaque Texture enabled. The material must stay in the
// Transparent queue: the boat's WaterMask (queue Transparent-1) hides the water inside the hull.
Shader "FishingVR/LakeWater"
{
    Properties
    {
        [Header(Water colour)]
        _ShallowColor ("Shallow Tint", Color) = (0.72, 0.88, 0.80, 1)
        _DeepColor ("Deep Colour", Color) = (0.035, 0.11, 0.115, 1)
        _Visibility ("Visibility (m)", Range(0.2, 10)) = 2
        _ShoreTintDepth ("Shore Tint Fade Depth (m)", Range(0.01, 2)) = 0.3

        [Header(Surface ripples)]
        [NoScaleOffset][Normal] _NormalMap ("Ripple Normal Map", 2D) = "bump" {}
        _RippleTileA ("Large Ripples Tile Size (m)", Float) = 7
        _RippleTileB ("Small Ripples Tile Size (m)", Float) = 2.3
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.3
        _WindSpeed ("Ripple Drift Speed (m/s)", Range(0, 1)) = 0.12
        _WindDirection ("Wind Direction (degrees)", Range(0, 360)) = 35
        _RippleFadeDistance ("Ripple Fade Distance (m)", Float) = 120

        [Header(Reflection and refraction)]
        _Smoothness ("Smoothness", Range(0.5, 1)) = 0.93
        _ReflectionStrength ("Sky Reflection", Range(0, 1)) = 0.9
        _SpecularStrength ("Sun Highlight", Range(0, 4)) = 1.2
        _Refraction ("Refraction Distortion", Range(0, 0.1)) = 0.025

        [Header(Foam)]
        [NoScaleOffset] _FoamTex ("Foam Noise", 2D) = "white" {}
        _FoamColor ("Foam Colour", Color) = (0.92, 0.95, 0.95, 1)
        _FoamTileSize ("Foam Tile Size (m)", Float) = 1.6
        _ShoreFoamDepth ("Shore Foam Depth (m)", Range(0, 1)) = 0.12
        _ShoreFoamStrength ("Shore Foam Strength", Range(0, 1)) = 0.55

        [Header(Interactive ripples from WaterRipples)]
        _InteractiveStrength ("Interactive Ripple Strength", Range(0, 4)) = 1
        _WakeFoamStrength ("Wake Foam Strength", Range(0, 2)) = 1
        _CrestFoam ("Ripple Crest Foam", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LakeWater"
            Tags { "LightMode" = "UniversalForward" }

            // The colour behind the water is composited in the shader (opaque texture), so no blending.
            Blend One Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamTex);   SAMPLER(sampler_FoamTex);

            // Set globally by WaterRipples (FishingVR.Water). _WaterRippleArea.w = 0 when there is no simulation.
            TEXTURE2D(_WaterRippleTex); SAMPLER(sampler_WaterRippleTex);
            float4 _WaterRippleArea;   // xy = world XZ of the area's min corner, z = 1 / area size, w = enabled
            float4 _WaterRippleParams; // x = texel size in UV, y = texel size in metres

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _Visibility;
                float _ShoreTintDepth;
                float _RippleTileA;
                float _RippleTileB;
                half _RippleStrength;
                float _WindSpeed;
                float _WindDirection;
                float _RippleFadeDistance;
                half _Smoothness;
                half _ReflectionStrength;
                half _SpecularStrength;
                half _Refraction;
                half4 _FoamColor;
                float _FoamTileSize;
                float _ShoreFoamDepth;
                half _ShoreFoamStrength;
                half _InteractiveStrength;
                half _WakeFoamStrength;
                half _CrestFoam;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
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

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // Interactive ripples: height gradient (slope) and wake foam from the simulation texture.
            void SampleInteractiveRipples(float2 xz, out float2 slope, out float foam)
            {
                slope = 0;
                foam = 0;
                if (_WaterRippleArea.w < 0.5)
                    return;

                float2 uv = (xz - _WaterRippleArea.xy) * _WaterRippleArea.z;
                float edge = min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y));
                if (edge <= 0.0)
                    return;
                float fade = saturate(edge * 12.0); // Fade out near the border of the simulated area.

                float d = _WaterRippleParams.x;
                float4 c = SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv, 0);
                float hL = SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv - float2(d, 0), 0).r;
                float hR = SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv + float2(d, 0), 0).r;
                float hD = SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv - float2(0, d), 0).r;
                float hU = SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv + float2(0, d), 0).r;

                slope = float2(hR - hL, hU - hD) / (2.0 * _WaterRippleParams.y) * fade;
                foam = c.b * fade;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                float3 toCamera = _WorldSpaceCameraPos - positionWS;
                float viewDistance = length(toCamera);
                float3 V = toCamera / max(viewDistance, 1e-4);
                float2 xz = positionWS.xz;

                // ---- Normal: two drifting ripple layers + interactive ripples -----------------------------
                float windAngle = radians(_WindDirection);
                float2 wind = float2(cos(windAngle), sin(windAngle));
                float2 crossWind = float2(-wind.y, wind.x);
                float t = _Time.y * _WindSpeed;

                float2 uvA = (xz + wind * t) / _RippleTileA;
                float2 uvB = (xz + (wind * 0.6 + crossWind * 0.8) * t * 1.7) / _RippleTileB;
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB));

                // Fade the static ripples with distance (less shimmer / tiling far away).
                half distanceFade = lerp(0.35, 1.0, saturate(1.0 - viewDistance / _RippleFadeDistance));
                float2 slope = -(nA.xy + nB.xy) * _RippleStrength * distanceFade; // tangent XY = world XZ

                float2 rippleSlope;
                float wakeFoam;
                SampleInteractiveRipples(xz, rippleSlope, wakeFoam);
                slope += rippleSlope * _InteractiveStrength;

                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));

                // ---- Depth behind the water --------------------------------------------------------------
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);

                // Refraction: offset the lookup with the surface slope. Weaker far away.
                float2 refractUV = screenUV + slope * (_Refraction / max(1.0, surfaceEyeDepth * 0.25));
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(refractUV), _ZBufferParams);
                if (sceneEyeDepth < surfaceEyeDepth)
                {
                    // The offset picked something in front of the water (a dock post, the hull...): don't refract.
                    refractUV = screenUV;
                    sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                }
                half3 sceneColor = SampleSceneColor(refractUV);

                // Point of the scene seen through the water, on the same view ray.
                float3 scenePositionWS = _WorldSpaceCameraPos - toCamera * (sceneEyeDepth / surfaceEyeDepth);
                float waterPath = max(0.0, sceneEyeDepth - surfaceEyeDepth) * (viewDistance / surfaceEyeDepth);
                float waterDepth = max(0.0, positionWS.y - scenePositionWS.y); // Vertical depth to the bottom / object.

                // Fade every surface effect out at the very edge, so the shoreline has no hard line.
                half edgeSoftness = saturate(waterDepth / 0.04);

                // ---- Lighting ------------------------------------------------------------------------------
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half shadow = mainLight.shadowAttenuation;
                half3 ambient = SampleSH(half3(0, 1, 0));
                half sunHeight = saturate(mainLight.direction.y);
                half3 volumeLight = ambient + mainLight.color * sunHeight * lerp(0.45, 1.0, shadow);

                // ---- Underwater colour (absorption) --------------------------------------------------------
                half transmittance = exp(-3.0 * waterPath / max(_Visibility, 0.01));
                half3 shoreTint = lerp(half3(1, 1, 1), _ShallowColor.rgb, saturate(waterDepth / _ShoreTintDepth));
                half3 deepColor = _DeepColor.rgb * volumeLight;
                float3 color = lerp(deepColor, sceneColor * shoreTint, transmittance);

                // ---- Reflection (Fresnel) and sun highlight --------------------------------------------------
                half NdotV = saturate(dot(N, V));
                half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                fresnel *= _ReflectionStrength * edgeSoftness;

                half perceptualRoughness = 1.0 - _Smoothness;
                half3 reflection = GlossyEnvironmentReflection(reflect(-V, N), perceptualRoughness, 1.0);
                color = lerp(color, reflection, fresnel);

                float3 H = normalize(mainLight.direction + V);
                float NdotH = saturate(dot(N, H));
                float roughness = max(perceptualRoughness * perceptualRoughness, 0.003);
                float a2 = roughness * roughness;
                float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float ggx = a2 / (PI * d * d);
                half specular = min(ggx * 0.25, 60.0) * saturate(dot(N, mainLight.direction)) * _SpecularStrength;
                color += mainLight.color * specular * shadow * edgeSoftness;

                // ---- Foam ----------------------------------------------------------------------------------
                float2 foamUV = xz / _FoamTileSize;
                half foamNoise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV + wind * t * 0.25).r * 0.6
                               + SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV * 0.47 - crossWind * t * 0.15).r * 0.4;

                // Shore / contact foam: strongest where the water is shallowest, broken up by the noise.
                half shore = 1.0 - saturate(waterDepth / max(_ShoreFoamDepth, 1e-3));
                half shoreFoam = saturate((shore * 1.4 - (1.0 - foamNoise)) * 2.5) * _ShoreFoamStrength * edgeSoftness;

                // Wake foam (written by the boat / splashes) and foam on steep ripple crests.
                half wake = saturate(wakeFoam * _WakeFoamStrength + length(rippleSlope) * _CrestFoam);
                half wakeFoamAmount = saturate((wake * 1.3 - (1.0 - foamNoise)) * 2.0) * wake;

                half foam = saturate(shoreFoam + wakeFoamAmount);
                half3 foamColor = _FoamColor.rgb * volumeLight;
                color = lerp(color, foamColor, foam);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
