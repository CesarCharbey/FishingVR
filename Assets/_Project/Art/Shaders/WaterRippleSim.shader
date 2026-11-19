// One step of the interactive ripple simulation (driven by WaterRipples.cs, never put on a material).
//
// The texture covers a square of the lake around the player. Each texel stores:
//   R = water height now (m), G = height at the previous step, B = foam amount (0-1).
// Step = discrete 2D wave equation (each point is pulled towards the average of its neighbours, keeping
// its momentum), with damping, plus the disturbances ("splats") of this frame and foam decay.
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

            float _SplatCount;
            float4 _Splats[MAX_SPLATS];     // xy = centre (UV), z = radius (UV), w = height added (m)
            float4 _SplatFoam[MAX_SPLATS];  // x = foam added

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
                float h = c.r;
                float previous = c.g;
                float neighbours = State(uv - float2(d.x, 0)).r + State(uv + float2(d.x, 0)).r
                                 + State(uv - float2(0, d.y)).r + State(uv + float2(0, d.y)).r;

                float next = h + (h - previous) * _Damping + _Courant2 * (neighbours - 4.0 * h);
                float foam = c.b * _FoamDecay;

                [loop]
                int splatCount = min((int)_SplatCount, MAX_SPLATS);
                for (int k = 0; k < splatCount; k++)
                {
                    float2 offset = i.uv - _Splats[k].xy;
                    float r = _Splats[k].z;
                    float falloff = saturate(1.0 - dot(offset, offset) / (r * r));
                    falloff *= falloff;
                    next += _Splats[k].w * falloff;
                    foam += _SplatFoam[k].x * falloff;
                }

                // Absorbing border: waves fade out instead of bouncing back from the edge of the area.
                float edge = min(min(i.uv.x, i.uv.y), min(1.0 - i.uv.x, 1.0 - i.uv.y));
                float absorb = lerp(0.85, 1.0, saturate(edge / _EdgeWidth));

                return float4(next * absorb, h * absorb, saturate(foam), 0);
            }
            ENDCG
        }
    }

    FallBack Off
}
