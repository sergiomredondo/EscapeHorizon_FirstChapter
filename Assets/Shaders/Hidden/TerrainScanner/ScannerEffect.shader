Shader "Hidden/ScannerWorld"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            TEXTURE2D(_GridTex);
            SAMPLER(sampler_GridTex);

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            float4 _ScannerCenter; // xyz = world pos
            float _ScanRadius;
            float _ScanWidth;
            float3 _ScanColor;
            float _GridScale;
            float _Intensity;
            float _maxDistance;
            float _FadeDecrement;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;

                float2 pos = float2(
                    (v.vertexID == 2) ? 3.0 : -1.0,
                    (v.vertexID == 1) ? 3.0 : -1.0
                );

                o.positionCS = float4(pos, 0.0, 1.0);
                o.uv = pos * 0.5 + 0.5;

                return o;
            }

            float3 ReconstructWorld(float2 uv)
            {
                float depth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;

                return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                
                //float4 clip = float4(uv * 2.0 - 1.0, depth, 1.0);

                //float4 view = mul(UNITY_MATRIX_I_P, clip);
                //view /= view.w;

                //float4 world = mul(UNITY_MATRIX_I_V, view);

                //return world.xyz;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif
    
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                if (_ScanRadius < 0.0) return col;
    
                float3 worldPos = ReconstructWorld(uv);

                float2 noiseUV = i.uv + float2(_Time.y * 0.1, _Time.y * 0.1);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;

                float3 distortedWorldPos = worldPos + (noise * 0.15); 
                float dist = distance(distortedWorldPos, _ScannerCenter.xyz);

                float wavePattern = frac((_ScanRadius - dist) * _ScanWidth); 
                float ring = smoothstep(0.15, 0.0, wavePattern); 

                float masterMask = smoothstep(_ScanRadius, _ScanRadius - 0.5, dist);
                ring *= masterMask;

                float2 gridUV = worldPos.xz * (_GridScale * 0.1); 
                float3 gridTex = SAMPLE_TEXTURE2D(_GridTex, sampler_GridTex, gridUV).rgb;

                float3 gridDisplay = gridTex * masterMask * 0.4;
                float3 waveDisplay = ring * 1.5;

                float finalIntensity = _Intensity * _FadeDecrement;
                float3 effectColor = _ScanColor * (gridDisplay + waveDisplay) * finalIntensity;

                float opacity = max(ring, masterMask * 0.3);
                float3 finalColor = lerp(col.rgb, effectColor + col.rgb, opacity * finalIntensity * 0.85);

                return float4(finalColor, 1);
            }

            ENDHLSL
        }
    }
}