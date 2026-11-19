// Stylized lake surface for URP (cartoon / low-poly look, VR single-pass instanced ready).
//
// Purely visual: the physics keeps using FlatWaterSurface (flat plane), so the surface is never displaced,
// only its normal is perturbed (drifting ripple maps + interactive ripples from WaterRipples).
//
// Look, per pixel:
// 1. Colour gradient with the depth of the water: light turquoise over the shallows, darker lake blue-green
//    further out (optionally in flat bands). The bottom and the fish stay visible through the water, wobbling
//    with the ripples, and fade out with the distance travelled in the water ("Visibility").
// 2. A net of wavy light lines drifting on the surface (stylized caustics), distorted by the ripples.
// 3. Crisp white foam: lace along the shore, a solid jagged ring around everything poking out of the water
//    (rocks, dock posts, hull), and the rings / wake drawn by the interactive ripples.
// 4. A light tint towards the horizon (stylized Fresnel) instead of a realistic mirror reflection.
//
// Requirements: URP asset with Depth Texture and Opaque Texture enabled. The material must stay in the
// Transparent queue: the boat's WaterMask (queue Transparent-1) hides the water inside the hull.
Shader "FishingVR/LakeWater"
{
    Properties
    {
        [Header(Water colour)]
        _ShallowColor ("Shallow Colour", Color) = (0.36, 0.78, 0.70, 1)
        _DeepColor ("Deep Colour", Color) = (0.05, 0.30, 0.40, 1)
        _ColorDepth ("Depth of the Deep Colour (m)", Range(0.2, 15)) = 3.5
        _ColorBands ("Colour Bands (0 = smooth)", Range(0, 8)) = 0
        _Visibility ("Visibility (m)", Range(0.2, 10)) = 1.8
        _ShallowOpacity ("Shallow Opacity", Range(0, 1)) = 0.25

        [Header(Horizon tint)]
        _HorizonColor ("Horizon Colour", Color) = (0.55, 0.82, 0.86, 1)
        _HorizonStrength ("Horizon Strength", Range(0, 1)) = 0.55
        _HorizonPower ("Horizon Falloff", Range(1, 10)) = 4
        _SkyReflection ("Sky Colour In Horizon", Range(0, 1)) = 0.3

        [Header(Ripples and distortion)]
        [NoScaleOffset][Normal] _NormalMap ("Ripple Normal Map", 2D) = "bump" {}
        _RippleTileA ("Large Ripples Tile Size (m)", Float) = 7
        _RippleTileB ("Small Ripples Tile Size (m)", Float) = 2.3
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _WindSpeed ("Drift Speed (m/s)", Range(0, 1)) = 0.12
        _WindDirection ("Wind Direction (degrees)", Range(0, 360)) = 35
        _Refraction ("Underwater Wobble", Range(0, 0.1)) = 0.035

        [Header(Light lines)]
        [NoScaleOffset] _PatternTex ("Line Pattern", 2D) = "black" {}
        _PatternColor ("Line Colour (alpha = strength)", Color) = (0.85, 1, 0.98, 0.3)
        _PatternTileSize ("Line Pattern Tile Size (m)", Float) = 3.5
        _PatternCutoff ("Line Thickness", Range(0.05, 0.95)) = 0.5
        _PatternWarp ("Line Wobble", Range(0, 2)) = 0.6
        _PatternDeepFade ("Lines Kept Over Deep Water", Range(0, 1)) = 0.45
        _PatternFadeDistance ("Lines Fade Distance (m)", Float) = 60

        [Header(Foam)]
        [NoScaleOffset] _FoamTex ("Foam Noise", 2D) = "white" {}
        _FoamColor ("Foam Colour", Color) = (0.96, 0.99, 0.98, 1)
        _FoamTileSize ("Foam Tile Size (m)", Float) = 1.4
        _ShoreFoamDepth ("Shore Foam Depth (m)", Range(0, 2)) = 0.45
        _ShoreFoamDensity ("Shore Foam Density", Range(0, 1)) = 0.55
        _RingFoamDepth ("Contact Ring Depth (m)", Range(0, 0.5)) = 0.07

        [Header(Lighting)]
        _LightInfluence ("Sun and Sky Influence", Range(0, 1)) = 0.5
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.35
        _GlintStrength ("Sun Glints", Range(0, 2)) = 0.6
        _GlintSize ("Sun Glints Size", Range(0.9, 0.999)) = 0.985

        [Header(Interactive ripples from WaterRipples)]
        _InteractiveStrength ("Interactive Ripple Strength", Range(0, 4)) = 1
        _WakeFoamStrength ("Wake Foam Strength", Range(0, 4)) = 1.5
        _CrestFoam ("Ripple Crest Foam", Range(0, 4)) = 1.2
        _RippleFoamCutoff ("Ripple Foam Threshold", Range(0.02, 1)) = 0.25
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

            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamTex);    SAMPLER(sampler_FoamTex);
            TEXTURE2D(_PatternTex); SAMPLER(sampler_PatternTex);

            // Set globally by WaterRipples (FishingVR.Water). _WaterRippleArea.w = 0 when there is no simulation.
            TEXTURE2D(_WaterRippleTex); SAMPLER(sampler_WaterRippleTex);
            float4 _WaterRippleArea;   // xy = world XZ of the area's min corner, z = 1 / area size, w = enabled
            float4 _WaterRippleParams; // x = texel size in UV, y = texel size in metres

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _ColorDepth;
                float _ColorBands;
                float _Visibility;
                half _ShallowOpacity;
                half4 _HorizonColor;
                half _HorizonStrength;
                half _HorizonPower;
                half _SkyReflection;
                float _RippleTileA;
                float _RippleTileB;
                half _RippleStrength;
                float _WindSpeed;
                float _WindDirection;
                half _Refraction;
                half4 _PatternColor;
                float _PatternTileSize;
                half _PatternCutoff;
                half _PatternWarp;
                half _PatternDeepFade;
                float _PatternFadeDistance;
                half4 _FoamColor;
                float _FoamTileSize;
                float _ShoreFoamDepth;
                half _ShoreFoamDensity;
                float _RingFoamDepth;
                half _LightInfluence;
                half _ShadowStrength;
                half _GlintStrength;
                half _GlintSize;
                half _InteractiveStrength;
                half _WakeFoamStrength;
                half _CrestFoam;
                half _RippleFoamCutoff;
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

            // Anti-aliased step (x >= edge): a crisp cartoon edge that does not shimmer (about one pixel wide).
            float AAStep(float edge, float x)
            {
                float v = x - edge;
                float w = max(fwidth(v) * 0.75, 1e-4);
                return smoothstep(-w, w, v);
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

                // ---- Ripples: two drifting normal layers + interactive ripples -----------------------------
                float windAngle = radians(_WindDirection);
                float2 wind = float2(cos(windAngle), sin(windAngle));
                float2 crossWind = float2(-wind.y, wind.x);
                float t = _Time.y * _WindSpeed;

                float2 uvA = (xz + wind * t) / _RippleTileA;
                float2 uvB = (xz + (wind * 0.6 + crossWind * 0.8) * t * 1.7) / _RippleTileB;
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB));
                float2 slope = -(nA.xy + nB.xy) * _RippleStrength; // tangent XY = world XZ

                float2 rippleSlope;
                float wakeFoam;
                SampleInteractiveRipples(xz, rippleSlope, wakeFoam);
                slope += rippleSlope * _InteractiveStrength;

                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));

                // ---- What lies under the surface -----------------------------------------------------------
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);

                // Wobble of the underwater view, weaker far away.
                float2 refractUV = screenUV + slope * (_Refraction / max(1.0, surfaceEyeDepth * 0.25));
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(refractUV), _ZBufferParams);
                if (sceneEyeDepth < surfaceEyeDepth)
                {
                    // The offset picked something in front of the water (a dock post, the hull...): don't wobble.
                    refractUV = screenUV;
                    sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                }
                half3 sceneColor = SampleSceneColor(refractUV);

                // Point of the scene seen through the water, on the same view ray. Depth for the foam and the
                // colour uses the unwobbled lookup, so their edges hug the shapes exactly.
                float straightEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float3 straightScene = _WorldSpaceCameraPos - toCamera * (straightEyeDepth / surfaceEyeDepth);
                float waterDepth = max(0.0, positionWS.y - straightScene.y);          // vertical depth
                float waterPath = max(0.0, sceneEyeDepth - surfaceEyeDepth) * (viewDistance / surfaceEyeDepth);
                half edgeSoftness = saturate(waterDepth / 0.03); // no hard line where the water meets the bank

                // ---- Lighting (kept light: the palette does most of the work) ------------------------------
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half shadow = lerp(1.0 - _ShadowStrength, 1.0, mainLight.shadowAttenuation);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 sunAndSky = ambient + mainLight.color * saturate(mainLight.direction.y);
                half3 light = lerp(half3(1, 1, 1), sunAndSky, _LightInfluence) * shadow;

                // ---- Water colour: gradient with depth ------------------------------------------------------
                float depth01 = saturate(waterDepth / _ColorDepth);
                depth01 = 1.0 - (1.0 - depth01) * (1.0 - depth01); // ease out: the shallows change colour fast
                if (_ColorBands >= 1.0)
                    depth01 = saturate(floor(depth01 * _ColorBands + 0.5) / _ColorBands);
                half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01) * light;

                // See-through: the bottom and the fish fade out with the distance travelled in the water.
                half opacity = 1.0 - exp(-3.0 * waterPath / max(_Visibility, 0.01));
                opacity = lerp(_ShallowOpacity, 1.0, opacity) * edgeSoftness;
                float3 color = lerp(sceneColor * lerp(half3(1, 1, 1), _ShallowColor.rgb, 0.35 * edgeSoftness), waterColor, opacity);

                // ---- Horizon tint (stylized Fresnel) --------------------------------------------------------
                half NdotV = saturate(dot(N, V));
                half horizon = pow(1.0 - NdotV, _HorizonPower) * _HorizonStrength * edgeSoftness;
                half3 sky = GlossyEnvironmentReflection(reflect(-V, N), 0.6, 1.0);
                half3 horizonColor = lerp(_HorizonColor.rgb * light, sky, _SkyReflection);
                color = lerp(color, horizonColor, horizon);

                // ---- Light lines (stylized caustics) --------------------------------------------------------
                float2 patternUV = xz / _PatternTileSize + slope * _PatternWarp;
                half lineA = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, patternUV + wind * t * 0.35).r;
                half lineB = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, patternUV * 0.71 + crossWind * t * 0.25 + 0.37).r;
                half lines = max(lineA, lineB * 0.85);
                half lineMask = AAStep(1.0 - _PatternCutoff, lines);
                lineMask *= lerp(1.0, _PatternDeepFade, depth01);
                lineMask *= saturate(1.0 - viewDistance / _PatternFadeDistance) * edgeSoftness;
                color = lerp(color, _PatternColor.rgb * light, lineMask * _PatternColor.a);

                // ---- Sun glints (small crisp sparkles) ------------------------------------------------------
                float3 H = normalize(mainLight.direction + V);
                half glint = AAStep(_GlintSize, saturate(dot(N, H))) * _GlintStrength * mainLight.shadowAttenuation;
                color += mainLight.color * glint * edgeSoftness;

                // ---- Foam (crisp white shapes) --------------------------------------------------------------
                float2 foamUV = xz / _FoamTileSize;
                half noise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV + wind * t * 0.3).r * 0.6
                           + SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV * 0.53 - crossWind * t * 0.2).r * 0.4;
                half lace = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, xz / (_FoamTileSize * 1.3) + slope * 0.3 + wind * t * 0.2).r;

                // Shore lace: solid at the water's edge, breaking into a lacy net further out.
                half shore = 1.0 - saturate(waterDepth / max(_ShoreFoamDepth, 1e-3));
                half shoreFoam = AAStep(1.0 - shore * _ShoreFoamDensity * 1.6, lace * 0.75 + noise * 0.35) * step(0.001, shore);

                // Contact ring: a solid jagged band around everything that pokes out of the water.
                half ring = AAStep(waterDepth, _RingFoamDepth * (0.55 + 0.9 * noise));

                // Interactive ripples: crisp rings and wake trails.
                half wake = wakeFoam * _WakeFoamStrength + length(rippleSlope) * _CrestFoam;
                half wakeFoamMask = AAStep(_RippleFoamCutoff, wake * (0.65 + 0.7 * noise));

                half foam = saturate(max(max(shoreFoam, ring), wakeFoamMask));
                color = lerp(color, _FoamColor.rgb * light, foam * _FoamColor.a);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
