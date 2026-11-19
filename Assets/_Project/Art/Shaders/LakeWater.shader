// Stylized lake surface for URP (cartoon / low-poly look, VR single-pass instanced ready).
//
// Purely visual: the physics keeps using FlatWaterSurface (flat plane), so the surface is never displaced,
// only its normal is perturbed (drifting ripple maps + interactive ripples from WaterRipples).
//
// Look, per pixel:
// 1. Colour gradient with the depth of the water: light turquoise over the shallows, darker lake blue-green
//    further out (optionally in flat bands). The bottom and the fish stay visible through the water, wobbling
//    with the ripples, and fade out with the distance travelled in the water ("Visibility").
// 2. A net of thick, soft light bands drifting on the surface, distorted by the ripples, and caustics
//    dancing on the bottom of the shallows (T_Water_Caustics: R/G = cell-edge distance fields, B = blobs).
// 3. Crisp white foam: cellular lace along the shore, a solid jagged ring around everything poking out of the water
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

        [Header(Light lines and caustics)]
        [NoScaleOffset] _PatternTex ("Caustics (R G = cell edge fields, B = blobs)", 2D) = "black" {}
        _PatternColor ("Line Colour (alpha = strength)", Color) = (0.85, 1, 0.98, 0.3)
        _PatternTileSize ("Line Pattern Tile Size (m)", Float) = 3.5
        _PatternCutoff ("Line Thickness", Range(0.02, 0.95)) = 0.5
        _PatternSoftness ("Line Softness", Range(0, 0.5)) = 0.15
        _PatternWarp ("Line Wobble", Range(0, 2)) = 0.6
        _PatternDeepFade ("Lines Kept Over Deep Water", Range(0, 1)) = 0.45
        _PatternFadeDistance ("Lines Fade Distance (m)", Float) = 60
        _FloorCaustics ("Caustics On The Bottom", Range(0, 2)) = 0.6
        _FloorCausticsTile ("Bottom Caustics Tile Size (m)", Float) = 2.5
        _FloorCausticsDepth ("Bottom Caustics Fade Depth (m)", Range(0.1, 10)) = 2.5

        [Header(Foam)]
        [NoScaleOffset] _FoamTex ("Foam Noise", 2D) = "white" {}
        _FoamColor ("Foam Colour", Color) = (0.96, 0.99, 0.98, 1)
        _FoamTileSize ("Foam Tile Size (m)", Float) = 1.4
        _ShoreFoamDepth ("Shore Foam Depth (m)", Range(0, 2)) = 0.45
        _ShoreFoamDensity ("Shore Foam Density", Range(0, 1)) = 0.55
        _ShoreFoamTile ("Shore Lace Tile Size (m)", Float) = 2
        _ShoreFoamBreathing ("Shore Foam Lapping Speed", Range(0, 3)) = 0.8
        _RingFoamDepth ("Contact Ring Depth (m)", Range(0, 0.5)) = 0.07
        _ShoreFade ("Fade Into The Bank (m of depth)", Range(0.01, 0.6)) = 0.15
        _BedTolerance ("Lake Bed Match Tolerance (m)", Range(0.01, 0.3)) = 0.08

        [Header(Lake bed depth baked by FishingVR Eau Calculer la profondeur du lac)]
        [NoScaleOffset] _BedDepthTex ("Lake Bed Depth (baked)", 2D) = "black" {}
        _BedDepthArea ("Lake Bed Area (min X, min Z, 1/size X, 1/size Z)", Vector) = (0, 0, 0, 0)

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
            TEXTURE2D(_BedDepthTex); SAMPLER(sampler_BedDepthTex);

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
                half _PatternSoftness;
                half _PatternWarp;
                half _PatternDeepFade;
                float _PatternFadeDistance;
                half _FloorCaustics;
                float _FloorCausticsTile;
                float _FloorCausticsDepth;
                half4 _FoamColor;
                float _FoamTileSize;
                float _ShoreFoamDepth;
                half _ShoreFoamDensity;
                float _ShoreFoamTile;
                half _ShoreFoamBreathing;
                float _RingFoamDepth;
                float _ShoreFade;
                float _BedTolerance;
                float4 _BedDepthArea;
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
            // The width is capped: where the input jumps between two pixels (the silhouette of something under
            // the water, where the depth jumps by metres), an uncapped width turns the step into 0.5 on both
            // sides and draws a pale outline around every submerged object.
            float AAStep(float edge, float x)
            {
                float v = x - edge;
                float w = clamp(fwidth(v) * 0.75, 1e-4, 0.04);
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
                half edgeSoftness = saturate(waterDepth / _ShoreFade); // fades into the bank, no hard line
                // Vertical surfaces (posts, hull, propeller, rock sides) make the depth jump from pixel to pixel.
                half depthSlope = fwidth(waterDepth) / max(fwidth(xz.x) + fwidth(xz.y), 1e-4);
                half steep = saturate((depthSlope - 0.6) / 0.8);

                // Depth of the lake bed (baked by LakeDepthBaker: terrain and rocks only, 8 bits = -1..5 m). What we see through the water is either
                // the bed itself (keep the exact per-pixel depth) or something above it: hull, post, fish... Those
                // must not be taken for a beach (no shore foam, no caustics, no shallow colour on them).
                float bedDepth = waterDepth;
                half onBed = 1.0;     // flat-ish bed (caustics, shore foam, colour from the exact depth)
                half bakedBed = 1.0;  // part of the baked bed, vertical sides included (rocks: contact ring)
                if (_BedDepthArea.z > 0.0)
                {
                    float2 bedUV = (xz - _BedDepthArea.xy) * _BedDepthArea.zw;
                    float2 sceneBedUV = (straightScene.xz - _BedDepthArea.xy) * _BedDepthArea.zw;
                    if (all(bedUV > 0.0) && all(bedUV < 1.0))
                    {
                        float bakedHere = SAMPLE_TEXTURE2D_LOD(_BedDepthTex, sampler_BedDepthTex, bedUV, 0).r * 6.0 - 1.0;
                        float bakedThere = SAMPLE_TEXTURE2D_LOD(_BedDepthTex, sampler_BedDepthTex, saturate(sceneBedUV), 0).r * 6.0 - 1.0;
                        // The visible point is the bed itself: within a few cm of the baked depth (not the bottom
                        // of a post or the propeller above a shallow bed), and not a vertical surface.
                        // Soft test (never a hard switch: the low-poly terrain faces sit a few cm off the
                        // smoothly interpolated bake, a hard test drew patches shaped like the triangles).
                        bakedBed = saturate(1.0 - (bakedThere - _BedTolerance - waterDepth) / 0.25);
                        onBed = bakedBed * (1.0 - steep);
                        // Colour and shore foam always follow the baked bed under this point: continuous.
                        bedDepth = max(bakedHere, 0.0);
                    }
                }

                // ---- Caustics on the bottom: light bands dancing on the sand, fading with depth -------------
                {
                    float2 floorUV = straightScene.xz / _FloorCausticsTile + slope * 0.4;
                    half cA = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, floorUV + wind * t * 0.6).r;
                    half cB = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, floorUV * 0.77 - crossWind * t * 0.45 + 0.41).g;
                    half caustic = smoothstep(0.55, 0.9, max(cA, cB)) + smoothstep(0.75, 0.95, min(cA, cB)) * 0.5;
                    half floorFade = (1.0 - saturate(waterDepth / _FloorCausticsDepth)) * edgeSoftness * onBed
                                   * saturate(1.0 - viewDistance / _PatternFadeDistance);
                    sceneColor *= 1.0 + caustic * floorFade * _FloorCaustics;
                }

                // ---- Lighting (kept light: the palette does most of the work) ------------------------------
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half shadow = lerp(1.0 - _ShadowStrength, 1.0, mainLight.shadowAttenuation);
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 sunAndSky = ambient + mainLight.color * saturate(mainLight.direction.y);
                half3 light = lerp(half3(1, 1, 1), sunAndSky, _LightInfluence) * shadow;

                // ---- Water colour: gradient with depth ------------------------------------------------------
                float depth01 = saturate(bedDepth / _ColorDepth);
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
                half lines = max(lineA, lineB * 0.9);
                // Thick, soft-edged organic bands (the texture is a distance-to-edge field).
                half lineEdge = 1.0 - _PatternCutoff;
                half lineMask = smoothstep(lineEdge, lineEdge + _PatternSoftness + fwidth(lines), lines);
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
                float2 laceUV = xz / _ShoreFoamTile + slope * 0.25 + wind * t * 0.15;
                half lace = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, laceUV).r; // 1 on the cell edges
                half blobs = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, xz / (_ShoreFoamTile * 2.7) - crossWind * t * 0.1).b;

                // Shore lace: a net of white cells, dense at the water's edge (holes only in the middle of the
                // cells), thinning out into lines then nothing further out; it laps in and out slowly.
                half shore = 1.0 - saturate(bedDepth / max(_ShoreFoamDepth, 1e-3));
                half lapping = 0.75 + 0.25 * sin(_Time.y * _ShoreFoamBreathing + blobs * 6.2832);
                half amount = shore * _ShoreFoamDensity * lapping;
                // Never fully solid: the middle of the cells always stays open (lace, not a white band).
                half laceEdge = max(0.22, 1.0 - amount * 1.35 + (blobs - 0.5) * 0.65); // in patches
                half shoreFoam = AAStep(laceEdge, lace) * step(0.001, shore);
                // The last centimetres: the lace breaks up irregularly while the water fades into the bank,
                // so the waterline is not a clean cut.
                shoreFoam *= AAStep((0.05 + 0.9 * noise) * 0.75, waterDepth / _ShoreFade);

                // Contact ring: a solid jagged band around everything that pokes out of the water.
                // Only where the depth jumps (rocks, banks): a gently sloping beach gets the lace instead.
                // Thinner when seen from a low angle (the band would smear down the submerged part of the object).
                // Only on the lake bed itself (baked): not on the fish, the hull or the dock posts.
                half ring = AAStep(waterDepth, _RingFoamDepth * (0.4 + 0.9 * noise) * saturate(V.y * 2.0)) * steep * bakedBed;

                // Wake foam drawn as lace like the shore (dense right behind the propeller, thinning into lines
                // further back), never as a solid white mass.
                half wake = wakeFoam * _WakeFoamStrength + length(rippleSlope) * _CrestFoam;
                half wakeAmount = saturate((wake * (0.7 + 0.6 * noise) - _RippleFoamCutoff) / max(1.0 - _RippleFoamCutoff, 0.05));
                half wakeFoamMask = AAStep(max(0.45, 1.0 - wakeAmount * 0.75), lace) * step(0.001, wakeAmount);

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
