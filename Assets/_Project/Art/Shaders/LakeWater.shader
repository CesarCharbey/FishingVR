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
// 3. Crisp white foam: cellular lace along the shore, a solid jagged band all around everything fixed poking out of
//    the water (rocks, dock posts: distance to their waterline baked in T_Lake_Contact), and the rings / wake drawn
//    by the interactive ripples.
// 4. A light tint towards the horizon (stylized Fresnel) instead of a realistic mirror reflection.
// 5. Fishing spots (FishingSpot): inside each spot the water shivers, gets slightly darker (a school of fish
//    underneath) and catches more light lines and sun glints, with an irregular edge, scaled by the spot intensity.
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

        [Header(Contact foam around rocks and posts baked with the lake depth)]
        [NoScaleOffset] _ContactTex ("Distance To The Waterlines (baked)", 2D) = "white" {}
        _ContactFoamWidth ("Contact Foam Width (m)", Range(0, 1)) = 0.3
        _ContactFoamJagged ("Contact Foam Jaggedness", Range(0, 1)) = 0.7
        _ContactFoamNoiseTile ("Contact Foam Bubbles Tile Size (m)", Float) = 1.8
        _ContactLaceWidth ("Contact Lace Width Beyond The Band (m)", Range(0, 1.5)) = 0.3
        _ContactFoamBreathing ("Contact Foam Lapping Speed", Range(0, 3)) = 1.1
        _ContactSizeReference ("Contact Foam Full Width From Size (m, re-bake)", Range(0.1, 5)) = 0.6
        _ContactSmallScale ("Contact Foam Width On Small Objects (re-bake)", Range(0.05, 1)) = 0.35

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

        [Header(Wake look options being evaluated. 0 is off)]
        _WakeCore ("1. Dense Core Behind The Propeller", Range(0, 1)) = 0
        _WakeCoreThreshold ("1. Core From Churned Water Amount (1 at the propeller, fading and spreading behind)", Range(0.02, 1)) = 0.12
        _WakeLaceStyle ("2. Own Finer Cells, No Light Lines Under", Range(0, 1)) = 0
        _WakeLaceTile ("2. Wake Cell Size (m)", Float) = 1.1
        _WakeBubbleTint ("3. Bubble Cloud Tint", Range(0, 1)) = 0
        _WakeBubbleColor ("3. Bubble Cloud Colour", Color) = (0.6, 0.93, 0.9, 1)
        _WakeFarSmoothing ("4. Smooth The Lace Far Away", Range(0, 1)) = 0

        [Header(Fishing spots from FishingSpot)]
        _SpotColor ("Spot Tint (alpha = strength)", Color) = (0.03, 0.24, 0.28, 0.4)
        _SpotRipples ("Spot Shiver", Range(0, 2)) = 0.3
        _SpotRippleTile ("Spot Shiver Tile Size (m)", Float) = 1.3
        _SpotRippleSpeed ("Spot Shiver Speed", Range(0, 3)) = 0.7
        _SpotLines ("Spot Extra Light Lines", Range(0, 2)) = 0.25
        _SpotGlints ("Spot Extra Glints", Range(0, 1)) = 0.15
        _SpotEdgeNoise ("Spot Edge Irregularity", Range(0, 1)) = 0.35
        _SpotRingDensity ("Spot Rings Density", Range(0, 1)) = 0.18
        _SpotRingSize ("Spot Rings Max Radius (m)", Range(0.1, 2)) = 0.6
        _SpotRingPeriod ("Spot Rings Lifetime (s)", Range(0.5, 6)) = 3
        _SpotRingWidth ("Spot Rings Width (m)", Range(0.005, 0.2)) = 0.03
        _SpotRingCell ("Spot Rings Spacing (m)", Range(0.5, 5)) = 2.4
        _SpotRingOpacity ("Spot Rings Opacity", Range(0, 1)) = 0.35
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
            TEXTURE2D(_ContactTex);  SAMPLER(sampler_ContactTex);
            // Distance stored in _ContactTex (0..1 = 0..CONTACT_RANGE m). Must match LakeDepthBaker.ContactRange.
            #define CONTACT_RANGE 2.0

            // Set globally by WaterRipples (FishingVR.Water). _WaterRippleArea.w = 0 when there is no simulation.
            TEXTURE2D(_WaterRippleTex); SAMPLER(sampler_WaterRippleTex);
            float4 _WaterRippleArea;   // xy = world XZ of the area's min corner, z = 1 / area size, w = enabled
            float4 _WaterRippleParams; // x = texel size in UV, y = texel size in metres

            // Set globally by FishingSpot (FishingVR.Fishing): xy = centre XZ, z = radius (m), w = intensity (0-1).
            #define MAX_FISHING_SPOTS 8
            float4 _FishingSpots[MAX_FISHING_SPOTS];
            float _FishingSpotCount;

            // Set globally by WaterContactFoam (FishingVR.Water): moving objects at the surface (buoys, boat, bobber),
            // as their waterline: a circle, or an outline (edges in _ContactSegments).
            #define MAX_CONTACT_SHAPES 16
            #define MAX_CONTACT_SEGMENTS 64
            float4 _ContactShapesA[MAX_CONTACT_SHAPES]; // xy = centre XZ, z = radius (circle) or around the outline (m), w = width scale x strength
            float4 _ContactShapesB[MAX_CONTACT_SHAPES]; // x = first edge, y = edge count (0 = circle), z = size of the waterline (m)
            float4 _ContactSegments[MAX_CONTACT_SEGMENTS]; // outline edges: xz of both ends
            float _ContactShapeCount;

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
                float _ContactFoamWidth;
                half _ContactFoamJagged;
                float _ContactFoamNoiseTile;
                float _ContactLaceWidth;
                half _ContactFoamBreathing;
                float _ContactSizeReference;
                float _ContactSmallScale;
                float4 _BedDepthArea;
                half _LightInfluence;
                half _ShadowStrength;
                half _GlintStrength;
                half _GlintSize;
                half _InteractiveStrength;
                half _WakeFoamStrength;
                half _CrestFoam;
                half _RippleFoamCutoff;
                half _WakeCore;
                half _WakeCoreThreshold;
                half _WakeLaceStyle;
                float _WakeLaceTile;
                half _WakeBubbleTint;
                half4 _WakeBubbleColor;
                half _WakeFarSmoothing;
                half4 _SpotColor;
                half _SpotRipples;
                float _SpotRippleTile;
                half _SpotRippleSpeed;
                half _SpotLines;
                half _SpotGlints;
                half _SpotEdgeNoise;
                half _SpotRingDensity;
                float _SpotRingSize;
                float _SpotRingPeriod;
                float _SpotRingWidth;
                float _SpotRingCell;
                half _SpotRingOpacity;
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

            // Same without the cap, for smooth inputs (baked distances): stays about one pixel wide far away.
            float AAStepWide(float edge, float x)
            {
                float v = x - edge;
                float w = max(fwidth(v) * 0.75, 1e-4);
                return smoothstep(-w, w, v);
            }

            // Interactive ripples: height gradient (slope) and wake foam from the simulation texture.
            void SampleInteractiveRipples(float2 xz, out float2 slope, out float foam, out float churn)
            {
                slope = 0;
                foam = 0;
                churn = 0;
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
                churn = c.a * fade; // white water churned by a propeller (short-lived)
            }

            // Width of the band for an object of this size at the waterline (m), relative to the full width: thin posts,
            // buoys and pebbles get a narrower band than big rocks. Same formula as WaterContactFoam.BandScale (the
            // lake bake uses it for the fixed objects).
            float ContactBandScale(float size)
            {
                return clamp(sqrt(max(size, 0.0) / max(_ContactSizeReference, 0.01)), _ContactSmallScale, 1.0);
            }

            float SegmentDistance(float2 p, float4 s)
            {
                float2 ab = s.zw - s.xy;
                float h = saturate(dot(p - s.xy, ab) / max(dot(ab, ab), 1e-8));
                return length(p - (s.xy + ab * h));
            }

            // Distance to the waterline of the nearest moving object (WaterContactFoam), in metres of a full width
            // band (divided by the object's band scale, like the baked distances).
            float MovingContactDistance(float2 xz)
            {
                float best = CONTACT_RANGE;
                int count = min((int)_ContactShapeCount, MAX_CONTACT_SHAPES);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float4 a = _ContactShapesA[i];
                    float4 b = _ContactShapesB[i];
                    float scale = max(a.w * ContactBandScale(b.z), 1e-3);
                    float toCentre = length(xz - a.xy);
                    if ((toCentre - a.z) / scale >= best)
                        continue; // too far to matter (the outline lies within a.z of the centre)
                    float d;
                    if (b.y < 0.5)
                    {
                        d = max(toCentre - a.z, 0.0);
                    }
                    else
                    {
                        d = 1e4;
                        float leftMost = 1e4, rightMost = -1e4;
                        int first = (int)b.x;
                        int last = min(first + (int)b.y, MAX_CONTACT_SEGMENTS);
                        [loop]
                        for (int j = first; j < last; j++)
                        {
                            float4 s = _ContactSegments[j];
                            d = min(d, SegmentDistance(xz, s));
                            // Side of the edge the point is on (the outline is convex).
                            float side = (s.z - s.x) * (xz.y - s.y) - (s.w - s.y) * (xz.x - s.x);
                            leftMost = min(leftMost, side);
                            rightMost = max(rightMost, side);
                        }
                        // Inside the outline: against the object (hidden by it), so the band starts right at the hull
                        // with no strip of water in between.
                        if (leftMost >= 0.0 || rightMost <= 0.0)
                            d = 0.0;
                    }
                    best = min(best, d / scale);
                }
                return best;
            }

            // Fishing spots: 1 in the middle of a spot (times its intensity), fading out towards an irregular edge.
            // margin (m) widens every spot (used to know whether a ring centred inside may reach this point).
            half FishingSpotMask(float2 xz, half edgeNoise, float margin)
            {
                half mask = 0.0;
                int count = min((int)_FishingSpotCount, MAX_FISHING_SPOTS);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float4 spot = _FishingSpots[i];
                    if (spot.w <= 0.001)
                        continue;
                    float d = max(0.0, length(xz - spot.xy) - margin) / max(spot.z, 0.01);
                    d += (edgeNoise - 0.5) * _SpotEdgeNoise;
                    mask = max(mask, (1.0 - smoothstep(0.45, 1.0, d)) * spot.w);
                }
                return mask;
            }

            // Hash without sine (Dave Hoskins): stable for large world coordinates. The sine version returned
            // exactly 0 now and then far from the origin, which lit rings all over the lake.
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Fishing spot rings: soft white rings opening here and there in a spot (fish rising, bubbles
            // popping). The plane is cut in cells; a cell may hold one ring per cycle, at a random place, size and
            // time. Whether a ring shows depends on the spot strength at the ring's centre (not at the pixel), so
            // a ring is always whole, never cut by the edge of the spot. Nothing outside the spots.
            // aa = pixel footprint on the water (computed in frag, outside any branch).
            half FishingSpotRings(float2 xz, float viewDistance, float aa)
            {
                // No spot within reach of this point: nothing to draw (no derivative below, the early return is safe).
                if (FishingSpotMask(xz, 0.5, _SpotRingSize * 1.2) <= 0.001)
                    return 0.0;

                float cellSize = _SpotRingCell;
                float2 cell = floor(xz / cellSize);
                // Thicker far away so the rings stay readable (about one pixel at least).
                float width = max(_SpotRingWidth, viewDistance * 0.0025);
                half rings = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + float2(x, y);
                    float lifetime = _SpotRingPeriod * lerp(0.7, 1.4, Hash21(c + 5.7));
                    float cycle = _Time.y / lifetime + Hash21(c + 17.3);
                    float age = frac(cycle);
                    float index = floor(cycle);

                    float2 jitter = float2(Hash21(c + index * 0.71 + 3.1), Hash21(c + index * 0.53 + 7.9));
                    float2 center = (c + 0.15 + 0.7 * jitter) * cellSize;
                    half strength = FishingSpotMask(center, 0.5, 0.0);
                    half active = Hash21(c + index * 1.37 + 0.5) < _SpotRingDensity * strength ? 1.0 : 0.0;

                    float size = _SpotRingSize * lerp(0.45, 1.0, Hash21(c + index * 2.11 + 9.2));
                    float radius = sqrt(age) * size; // opens fast, then slows down
                    float d = abs(length(xz - center) - radius);
                    half ring = 1.0 - smoothstep(width * 0.5, width * 0.5 + aa, d);
                    half fade = pow(1.0 - age, 1.5) * smoothstep(0.0, 0.08, age);
                    rings = max(rings, ring * fade * active);
                }
                return rings;
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
                float pixelFootprint = length(fwidth(xz)) + 1e-4; // size of a pixel on the water (m)

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

                // Sun glints use the normal without the spot's shiver (the shiver sprinkled glints all over the spot).
                float2 glintSlope = slope;

                // Fishing spot: the water shivers (two fast layers of small ripples crossing each other).
                half spotNoise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, xz / 7.0 + _Time.y * 0.02).r;
                half spot = FishingSpotMask(xz, spotNoise, 0.0);
                if (spot > 0.001)
                {
                    float shiverTime = _Time.y * _SpotRippleSpeed * 0.25;
                    half3 nS1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz / _SpotRippleTile + float2(shiverTime, shiverTime * 0.4)));
                    half3 nS2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz / (_SpotRippleTile * 0.63) - float2(shiverTime * 0.3, shiverTime * 1.1)));
                    slope += -(nS1.xy + nS2.xy) * _SpotRipples * 0.5 * spot;
                }

                float2 rippleSlope;
                float wakeFoam, wakeChurn;
                SampleInteractiveRipples(xz, rippleSlope, wakeFoam, wakeChurn);
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
                float contactDistance = CONTACT_RANGE; // to the nearest waterline of a rock, post... (baked)
                if (_BedDepthArea.z > 0.0)
                {
                    float2 bedUV = (xz - _BedDepthArea.xy) * _BedDepthArea.zw;
                    float2 sceneBedUV = (straightScene.xz - _BedDepthArea.xy) * _BedDepthArea.zw;
                    if (all(bedUV > 0.0) && all(bedUV < 1.0))
                    {
                        contactDistance = SAMPLE_TEXTURE2D_LOD(_ContactTex, sampler_ContactTex, bedUV, 0).r * CONTACT_RANGE;
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

                contactDistance = min(contactDistance, MovingContactDistance(xz)); // buoys, boat, bobber

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

                // Fishing spot: darker water, as over a school of fish (after the horizon tint, so it still reads
                // from a low point of view).
                color = lerp(color, _SpotColor.rgb * light, spot * _SpotColor.a * edgeSoftness);

                // Option 3: the cloud of bubbles under the water churned by the propeller turns it milky turquoise.
                half wakeRaw = saturate(wakeFoam * _WakeFoamStrength + length(rippleSlope) * _CrestFoam);
                // Soft all the way to its sides: it is a cloud under the surface, not foam on it.
                half bubbleCloud = smoothstep(0.0, 0.45, wakeChurn);
                color = lerp(color, _WakeBubbleColor.rgb * light, bubbleCloud * bubbleCloud * _WakeBubbleTint * edgeSoftness);

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
                lineMask = saturate(lineMask * (1.0 + spot * _SpotLines)); // livelier light over a spot
                // Option 2: no light lines under the wake (they are cells too: the foam read as over-exposed light).
                lineMask *= 1.0 - smoothstep(0.05, 0.3, wakeRaw) * _WakeLaceStyle;
                color = lerp(color, _PatternColor.rgb * light, lineMask * saturate(_PatternColor.a * (1.0 + spot * _SpotLines)));

                // ---- Sun glints (small crisp sparkles) ------------------------------------------------------
                float3 H = normalize(mainLight.direction + V);
                float3 glintN = normalize(float3(-(glintSlope.x + rippleSlope.x * _InteractiveStrength), 1.0, -(glintSlope.y + rippleSlope.y * _InteractiveStrength)));
                // More and bigger sparkles over a spot (the shivering water catches the sun).
                half glintSize = _GlintSize - 0.02 * spot * _SpotGlints;
                half glintStrength = _GlintStrength * (1.0 + 1.5 * spot * _SpotGlints);
                half glint = AAStep(glintSize, saturate(dot(glintN, H))) * glintStrength * mainLight.shadowAttenuation;
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

                // Contact foam: a solid jagged band hugging everything fixed that pokes out of the water (rocks,
                // dock posts, logs), all the way around whatever the point of view. It follows the baked distance
                // to their waterline, not the depth seen through the water, so nothing under the surface (fish,
                // hull, propeller) turns white. Just outside, it breaks up into a little lace; it laps in and out.
                // The edge is cut by round bubbles (foam noise, contrasted): scalloped outline, a few holes near the
                // edge and a few bubbles breaking away. Bubbles smaller than a few pixels would shimmer: the band
                // gets smoother far away.
                float2 bubbleUV = xz / _ContactFoamNoiseTile;
                half bubbles = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, bubbleUV + wind * t * 0.25).r * 0.7
                             + SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, bubbleUV * 2.3 - crossWind * t * 0.35 + 0.53).r * 0.3;
                bubbles = saturate((bubbles - 0.45) * 2.2 + 0.5);
                half jagAmount = _ContactFoamJagged * saturate(1.5 - pixelFootprint * 15.0);
                float bandWidth = _ContactFoamWidth * (1.0 + 0.1 * sin(_Time.y * _ContactFoamBreathing + blobs * 6.2832));
                bandWidth *= 1.0 + (bubbles - 0.5) * 2.0 * jagAmount;
                // A little wider far away so it stays readable (capped: towards the horizon a pixel covers metres of
                // water, the band would then swallow everything near the far shore).
                bandWidth = max(bandWidth, 0.0) + min(pixelFootprint, 0.06) * step(0.001, _ContactFoamWidth);
                // Nothing where no waterline is within reach (the stored distance is capped at CONTACT_RANGE).
                half contactBand = AAStepWide(contactDistance, bandWidth) * step(contactDistance, CONTACT_RANGE * 0.98);
                // A little broken lace just outside the band (threshold above 1 at the end: nothing further out).
                half laceFade = saturate((contactDistance - bandWidth) / max(_ContactLaceWidth, 1e-3));
                half contactLaceEdge = lerp(0.55, 1.05, laceFade) + (blobs - 0.5) * 0.25 * (1.0 - laceFade);
                half contactLace = AAStep(contactLaceEdge, lace) * step(0.001, _ContactLaceWidth);
                half contactFoam = max(contactBand, contactLace);

                // Wake foam drawn as lace like the shore (dense right behind the propeller, thinning into lines
                // further back), never as a solid white mass.
                half wake = wakeFoam * _WakeFoamStrength + length(rippleSlope) * _CrestFoam;
                half wakeAmount = saturate((wake * (0.7 + 0.6 * noise) - _RippleFoamCutoff) / max(1.0 - _RippleFoamCutoff, 0.05));
                // Option 2: the wake's own cells (the other cell field), finer than the shore's.
                half wakeLace = lace;
                if (_WakeLaceStyle > 0.5)
                    wakeLace = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, xz / _WakeLaceTile + slope * 0.25 + wind * t * 0.15 + 0.29).g;
                // Option 1 (below): near the propeller the lace thickens, between the solid core and the usual lace.
                half churnAmount = saturate(wakeChurn * 2.5) * _WakeCore;
                half laceAmount = max(wakeAmount, churnAmount);
                half wakeEdge = max(lerp(0.45, 0.25, churnAmount), 1.0 - laceAmount * 0.75);
                // Option 4, against the shimmer as the head moves: the lines get a full one-pixel soft edge (AAStep's
                // cap made them harder than a pixel), and where they get thinner than a couple of pixels they fade into
                // their average coverage, a soft pale trail.
                half wakeFoamMask = (_WakeFarSmoothing > 0.5 ? AAStepWide(wakeEdge, wakeLace) : AAStep(wakeEdge, wakeLace)) * step(0.001, laceAmount);
                half farSmooth = saturate((fwidth(wakeLace) - 0.08) / 0.15) * _WakeFarSmoothing;
                wakeFoamMask = lerp(wakeFoamMask, laceAmount * 0.4, farSmooth);
                // Option 1: the fresh foam right behind the propeller drawn like the contact foam: a solid core cut by
                // round bubbles, breaking up into the lace as it gets older.
                // From the churned water only (not the foam of the V's crests). 0 at the threshold, 1 at twice it.
                half coreField = saturate((wakeChurn - _WakeCoreThreshold) / max(_WakeCoreThreshold, 0.02));
                half wakeCore = AAStepWide(0.5, coreField + (bubbles - 0.5) * 2.0 * _ContactFoamJagged) * step(0.001, _WakeCore) * step(0.001, coreField);
                wakeFoamMask = max(wakeFoamMask, wakeCore * _WakeCore);

                half foam = saturate(max(max(max(shoreFoam, ring), contactFoam), wakeFoamMask));
                // Fishing spot rings: see-through, so they read as light ripples rather than white paint.
                half spotRings = FishingSpotRings(xz, viewDistance, pixelFootprint) * edgeSoftness;
                color = lerp(color, _FoamColor.rgb * light, spotRings * _SpotRingOpacity);

                color = lerp(color, _FoamColor.rgb * light, foam * _FoamColor.a);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
