Shader "Addition/Post-processing/VolumetricCloud"
{
    Properties
    {
        _BoxMin("Box Min", Vector) = (-50, -50, -50)
        _BoxMax("Box Max", Vector) = (50, 50, 50)
        _NoiseTex("Noise Texture", 3D) = "white" {}
        _NoiseScale("Noise Scale", Vector) = (1, 1, 1)
        _NoiseOffset("Noise Offset", Vector) = (0, 0, 0)
        _BlueNoiseTex("Blue Noise Texture", 2D) = "white" {}
        [HDR]_FogColor("Fog Color", Color) = (0.5, 0.5, 0.5, 1)
        _FogDensity("Fog Density", Range(0, 1)) = 0.1
        _Absorption("Absorption", Float) = 1
        _LightIntensity("Light Intensity", Float) = 1
        _Transmission("Transmission", Range(0, 1)) = 1
        _Reflection("Reflection", Range(0, 1)) = 0
        _Attenuation("Attenuation", Range(0, 1)) = 0.5
        _Contribution("Contribution", Range(0, 1)) = 0.5
        _PhaseAttenuation("Phase Attenuation", Range(0, 1)) = 0.5
        _StepCount("Step Count", Range(1, 128)) = 32
        _MinStepSize("Min Step Size", Range(0.05, 1)) = 0.05
        _LightSampleCount("Light Sample Count", Range(4, 32)) = 8
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
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Shader/Utils.hlsl"
            

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_BlueNoiseTex);
            SAMPLER(sampler_BlueNoiseTex);
            float4 _BlueNoiseTex_ST;
            float3 _BoxMin;
            float3 _BoxMax;
            float3 _NoiseScale;
            float3 _NoiseOffset;
            float4 _FogColor;
            float _HeightDecay;
            float _FogDensity;
            float _LightIntensity;
            float _Transmission;
            float _Reflection;
            float _Attenuation;
            float _Contribution;
            float _PhaseAttenuation;
            int _StepCount;
            int _LightSampleCount;
            float _MinStepSize;
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
                float f = (max(0, min(min(h - lower, upper - h), _EdgeFadeThreshold)) / _EdgeFadeThreshold);
                return f * f;
            }

            float SampleCloud(float3 pos)
            {
                float fade = EdgeFade(pos.y, _BoxMax.y, _BoxMin.y);
                pos.xy += _Time.y * 50;
                return saturate((SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, pos * _NoiseScale + _NoiseOffset, 0).r - 0.65) / 0.35).r * fade * _FogDensity;
            }

            // Henyey-Greenstein phase function: 描述云层边缘的散射方向
            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                float base = abs(1 + g2 - 2 * g * cosTheta);
                return 0.079577475 * (1 - g2) / (sqrt(base) * base);
            }

            // Beer-Powder：描述光线的衰减
            float BeerPowder(float d, float a)
            {
                return exp(-d * a) * (1 - exp(-d * 2 * a));
            }

            float MultipleOctaveScattering (float opticalDensity, float DOT)
            {
                float luminance = 0;
                const float octaves = 4.0;
                
                // Attenuation
                float a = 1.0;
                // Contribution
                float b = 1.0;
                // Phase attenuation
                float c = 1.0;
                
                float phase;
                
                for(float i = 0.0; i < octaves; i++){
                    // Two-lobed HG
                    phase = lerp(HenyeyGreenstein(DOT, -_Reflection * c), HenyeyGreenstein(DOT, _Transmission * c), 0.7);
                    luminance += b * phase * exp(-opticalDensity * a);
                    // Lower is brighter
                    a *= _Attenuation;
                    // Higher is brighter
                    b *= _Contribution;
                    c *= _PhaseAttenuation;
                }
                return luminance;
            }

            float LightRay(float3 p, float DOT, float3 lightDir, int stepCount)
            {
                float lightRayDistance = max(0, AABBRayIntersect(p, lightDir, _BoxMin, _BoxMax).y);
                float stepL = max(_MinStepSize, lightRayDistance / stepCount);
	            float lightRayDensity = 0.0;

	            // Collect total density along light ray.
	            for (int j = 0; j < _LightSampleCount; j++)
	            {
		            lightRayDensity += SampleCloud(p + lightDir * float(j) * stepL);
	            }
                
	            float beersLaw = MultipleOctaveScattering(lightRayDensity * stepL, DOT);
	            
                // Return product of Beer's law and powder effect depending on the 
                // view direction angle with the light direction.
                return beersLaw;
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
                float3 color = SampleSceneColor(i.texcoord).rgb;

                // ray marching
                float3 dir = normalize(distance);
	            float totalTransmittance = 1;
	            float3 totalLightPower = 0;
                float2 intersections = AABBRayIntersect(_WorldSpaceCameraPos.xyz, dir, _BoxMin, _BoxMax);
                intersections = clamp(intersections, 1e-5, length(distance));
                float distToStart = intersections.x;
                float totalDistance = intersections.y - distToStart;
                if (totalDistance <= 0) return float4(color, 1);
                float stepS = totalDistance / _StepCount; 
                distToStart += stepS * SAMPLE_TEXTURE2D(_BlueNoiseTex, sampler_BlueNoiseTex, i.texcoord).r;
                float3 p = _WorldSpaceCameraPos.xyz + distToStart * dir;
                float DOT = dot(dir, _MainLightPosition.xyz);
	            float phaseFunction = lerp(HenyeyGreenstein(DOT, -_Reflection), HenyeyGreenstein(DOT, _Transmission), 0.7);
                float3 sunLight = _MainLightColor.xyz * _LightIntensity;

	            for(float j = 0; j < totalDistance; j += stepS){
                    float density = SampleCloud(p);
                    if(density > 0.0 ){
                        float3 luminance = sunLight * phaseFunction * LightRay(p, DOT, _MainLightPosition.xyz, _LightSampleCount);
                        float3 transmittance = exp(-density * stepS);
                        totalLightPower += totalTransmittance * (luminance - luminance * transmittance); 
                        totalTransmittance *= transmittance;  
                        if(length(totalTransmittance) <= 0.001){
                            totalTransmittance = 0;
                            break;
                        }
                    }
		            p += stepS * dir;
	            }

                totalLightPower = 150 * totalLightPower + totalTransmittance * color;
	            return float4(totalLightPower, 1);
            }
            ENDHLSL
        }
    }
}
