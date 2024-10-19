Shader "Addition/Post-processing/VolumetricCloud_"
{
    Properties
    {
        _BoxMin("Box Min", Vector) = (-50, -50, -50)
        _BoxMax("Box Max", Vector) = (50, 50, 50)
        _NoiseTex("Noise Texture", 3D) = "white" {}
        _NoiseScale("Noise Scale", Vector) = (1, 1, 1)
        _NoiseOffset("Noise Offset", Vector) = (0, 0, 0)
        _BlueNoiseTex("Blue Noise Texture", 2D) = "white" {}
        _Density("Density", Range(0, 1)) = 0.1
        _SigmaAbsorption("Sigma Absorption", Vector) = (0, 0, 0)
        _SigmaScattering("Sigma Scattering", Vector) = (1, 1, 1)
        _LightIntensity("Light Intensity", Float) = 1
        _Transmission("Transmission", Range(0, 1)) = 1
        _Reflection("Reflection", Range(0, 1)) = 0
        _Attenuation("Attenuation", Range(0, 1)) = 0.5
        _Contribution("Contribution", Range(0, 1)) = 0.5
        _PhaseAttenuation("Phase Attenuation", Range(0, 1)) = 0.5
        _Exposure("Exposure", Float) = 150
        _StepCount("Step Count", Range(1, 128)) = 32
        _MinStepSize("Min Step Size", Range(0.05, 1)) = 0.05
        _LightSampleCount("Light Sample Count", Range(4, 32)) = 8
        _CloudBottom("Cloud Bottom", Float) = 100
        _EdgeFadeThreshold("Edge Fade Threshold", Range(1, 500)) = 50
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Shader/Include/Utils.hlsl"
            

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_BlueNoiseTex);
            SAMPLER(sampler_BlueNoiseTex);
            float4 _BlueNoiseTex_ST;
            float3 _BoxMin;
            float3 _BoxMax;
            float3 _NoiseScale;
            float3 _NoiseOffset;
            float _Density;
            float3 _SigmaAbsorption;
            float3 _SigmaScattering;
            float _LightIntensity;
            float _Transmission;
            float _Reflection;
            float _Attenuation;
            float _Contribution;
            float _PhaseAttenuation;
            float _Exposure;
            int _StepCount;
            int _LightSampleCount;
            float _MinStepSize;
            float _CloudBottom;
            float _EdgeFadeThreshold;

            struct VertexInfo
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragInfo
            {
                float4 positionCS : POSITION;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            FragInfo vert(VertexInfo input)
            {
                FragInfo output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv  = GetFullScreenTriangleTexCoord(input.vertexID);
                // ComputeWorldSpacePosition()
                output.positionCS = pos;
                output.texcoord   = uv;
                
                // // 计算视角空间viewdir, 这里不能进行单位化, 会导致插值错误
                // output.unVWS = ComputeWorldSpaceScreenPos(pos.xyz) - _WorldSpaceCameraPos;       // 为了实现物体与体积的遮挡关系，片元着色器中需要逐像素重建世界坐标，因此方向向量可以直接放在片元着色器中计算
                return output;
            }

            float EdgeFade(float h, float upper, float lower)
            {
                float f = max(0, min(min(h - lower, upper - h), _EdgeFadeThreshold) / _EdgeFadeThreshold);
                return f * f;
            }

            float SampleAtmosphere(float3 pos)
            {
                float fade = EdgeFade(pos.y, _BoxMax.y, _CloudBottom);
                float lin = lerp(0, 0.01, (pos.y - _BoxMin.y) / _CloudBottom);
                pos.xy += _Time.y * 50;
                return lerp(lin, saturate((SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, pos * _NoiseScale + _NoiseOffset, 0).r - 0.6) / 0.4).r, fade);
            }

            // Henyey-Greenstein phase function: 描述云层边缘的散射方向
            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                float base = abs(1 + g2 - 2 * g * cosTheta);
                return 0.079577475 * (1 - g2) / (sqrt(base) * base);
            }
            
            float3 MultipleOctaveScattering (float3 extinction, float DOT)
            {
                float3 lum;
                float a = 1;
                float b = 1;
                float c = 1;
                for (int i = 0; i < 4; i++)
                {
                    lum += b * lerp(HenyeyGreenstein(DOT, c * _Transmission), HenyeyGreenstein(DOT, c * (_Reflection - 1)), 0.7)
                        * exp(-extinction * a);
                    a *= _Attenuation;
                    b *= _Contribution;
                    c *= _PhaseAttenuation;
                }
                return lum;
            }

            float3 LightRay(float3 p, float DOT, float3 sigma, float3 lightDir, int stepCount)
            {
                float tmax = abs(AABBRayIntersect(p, lightDir, _BoxMin, _BoxMax)).y;
                float stepSize = max(_MinStepSize, tmax / stepCount);
                float dD = stepSize * _Density;

                float totalDensity = 0;
                for (float j = 0; j < tmax; j += stepSize)
                {
                    totalDensity += dD * SampleAtmosphere(p + j * lightDir);
                }
                return MultipleOctaveScattering(totalDensity * sigma, DOT);
            }

            float4 frag (FragInfo i) : SV_Target
            {
                // 重建世界坐标
                float depth = SampleSceneDepth(i.texcoord);
                #ifndef UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 worldPos = ComputeWorldSpacePosition(i.texcoord, depth, UNITY_MATRIX_I_VP);
                float3 distance = worldPos - _WorldSpaceCameraPos;

                // 采样颜色
                const float3 color = SampleSceneColor(i.texcoord).rgb;
                const float3 sigmaExtinction = max(1e-5, _SigmaScattering + _SigmaAbsorption);

                // ray marching
                float3 dir = normalize(distance);
                float2 intersections = AABBRayIntersect(_WorldSpaceCameraPos.xyz, dir, _BoxMin, _BoxMax);
                intersections = clamp(intersections, 1e-5, length(distance));
                float rayLength = intersections.y - intersections.x;
                if (rayLength <= 0) return float4(color, 1);
                
                float stepSize = max(_MinStepSize, rayLength / _StepCount);
                float offset = SAMPLE_TEXTURE2D(_BlueNoiseTex, sampler_BlueNoiseTex, i.texcoord).r * stepSize;
                float3 rayPos = _WorldSpaceCameraPos.xyz + (intersections.x + offset) * dir;
                float DOT = dot(dir, _MainLightPosition.xyz);
                float phase = lerp(HenyeyGreenstein(DOT, -_Reflection + 1), HenyeyGreenstein(DOT, _Transmission), 0.7);
                float dD = stepSize * _Density;
                float3 lightAttenuation = 1;
                float3 totalLightPower = 0;
                float3 sunLight = _MainLightColor.xyz * _LightIntensity;
                dir *= stepSize;
                for (float j = 0; j < rayLength; j += stepSize)
                {
                    rayPos += dir;
                    float density = dD * SampleAtmosphere(rayPos);
                    if (density > 1e-5)
                    {
                        float3 attenuation = exp(-density * sigmaExtinction);
                        lightAttenuation *= attenuation;
                        if (length(lightAttenuation) < 1e-5)
                        {
                            lightAttenuation = 0;
                            break;
                        }

                        float4 shadowCoord = TransformWorldToShadowCoord(rayPos);
                        float shadow = MainLightRealtimeShadow(shadowCoord);
                        float3 lightPower = LightRay(rayPos, DOT, sigmaExtinction, _MainLightPosition.xyz, _LightSampleCount) * shadow
                        * sunLight * phase * _SigmaScattering / sigmaExtinction;
                        totalLightPower += lightAttenuation * (lightPower - lightPower * attenuation); 
                    }
                }
                return float4(color * lightAttenuation + _Exposure * totalLightPower, 1);
            }
            ENDHLSL
        }
    }
}
