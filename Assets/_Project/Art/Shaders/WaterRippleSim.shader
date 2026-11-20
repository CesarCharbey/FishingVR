// One step of the interactive ripple simulation (driven by WaterRipples.cs, never put on a material).
//
// The texture covers a square of the lake around the player. Each texel stores:
//   R = water height now (m), G = height at the previous step, B = foam amount (0-1),
//   A = churned white water right behind a propeller (0-1, short-lived).
// Step = discrete 2D wave equation (each point is pulled towards the average of its neighbours, keeping
// its momentum), with damping, plus the disturbances ("splats") of this frame. Foam appears where the
// waves are steep or where a splat drops some, slowly spreads and fades.
// The area follows the player in whole texels: _Shift scrolls the content so the waves stay put in the world.
Shader "Hidden/FishingVR/WaterRippleSim"
{
    Properties
    {
        _MainTex ("State", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Step"

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_SPLATS 32

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float2 _Shift;        // UV offset to read the previous state from (area moved)
            float _Courant2;      // (wave speed * dt / texel)^2, < 0.5 for stability
            float _Damping;       // momentum kept per step (0-1)
            float _FoamDecay;     // foam kept per step (0-1)
            float _EdgeWidth;     // UV width of the absorbing border
            float _TexelWorld;    // texel size (m)
            float _SteepThreshold; // wave slope above which foam appears
            float _SteepFoam;     // foam added per step at twice the threshold slope
            float _FoamSpread;    // share of the neighbours' foam blended in per step (0-1)
            float _ChurnDecay;    // churned white water kept per step (0-1): fades much faster than the foam
            float _ChurnSpread;   // share of the neighbours' churn blended in per step (0-1), over a wider ring

            float _SplatCount;
            float4 _Splats[MAX_SPLATS];     // xy = centre (UV), z = radius (UV), w = height added (m)
            float4 _SplatFoam[MAX_SPLATS];  // x = foam added, y = churned white water added (propeller)

            float4 State(float2 uv)
            {
                if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)
                    return 0; // Outside the previous area: calm water.
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv + _Shift;
                float2 d = _MainTex_TexelSize.xy;

                float4 c = State(uv);
                float4 left = State(uv - float2(d.x, 0));
                float4 right = State(uv + float2(d.x, 0));
                float4 down = State(uv - float2(0, d.y));
                float4 up = State(uv + float2(0, d.y));

                float h = c.r;
                float previous = c.g;
                float neighbours = left.r + right.r + down.r + up.r;
                float next = h + (h - previous) * _Damping + _Courant2 * (neighbours - 4.0 * h);

                // Foam: slowly spreads to the neighbours and fades.
                float foam = lerp(c.b, (left.b + right.b + down.b + up.b) * 0.25, _FoamSpread) * _FoamDecay;
                // Churned white water right behind a propeller (A): its own channel, so the lake can draw it apart
                // from the foam of the wave crests; short-lived. It spreads sideways much faster than the foam (the
                // neighbours 1 and 3 texels away), so the wash opens into a cone as it fades, with soft sides.
                float2 far = d * 3.0;
                float churnRing = left.a + right.a + down.a + up.a
                                + State(uv - float2(far.x, 0)).a + State(uv + float2(far.x, 0)).a
                                + State(uv - float2(0, far.y)).a + State(uv + float2(0, far.y)).a;
                float churn = lerp(c.a, churnRing * 0.125, _ChurnSpread) * _ChurnDecay;

                // Steep waves break into foam (the arms of the boat's V wake, splashes). It then stays behind
                // the moving crest and fades, drawing foam lines along the wake.
                float slope = length(float2(right.r - left.r, up.r - down.r)) / (2.0 * _TexelWorld);
                foam += _SteepFoam * saturate((slope - _SteepThreshold) / max(_SteepThreshold, 1e-4));

                int splatCount = min((int)_SplatCount, MAX_SPLATS);
                [loop]
                for (int k = 0; k < splatCount; k++)
                {
                    float2 offset = i.uv - _Splats[k].xy;
                    float r = _Splats[k].z;
                    float falloff = saturate(1.0 - dot(offset, offset) / (r * r));
                    falloff *= falloff;
                    next += _Splats[k].w * falloff;
                    foam += _SplatFoam[k].x * falloff;
                    churn += _SplatFoam[k].y * falloff;
                }

                // Absorbing border: waves fade out instead of bouncing back from the edge of the area.
                float edge = min(min(i.uv.x, i.uv.y), min(1.0 - i.uv.x, 1.0 - i.uv.y));
                float absorb = lerp(0.85, 1.0, saturate(edge / _EdgeWidth));

                return float4(next * absorb, h * absorb, saturate(foam), saturate(churn));
            }
            ENDCG
        }
    }

    FallBack Off
}
