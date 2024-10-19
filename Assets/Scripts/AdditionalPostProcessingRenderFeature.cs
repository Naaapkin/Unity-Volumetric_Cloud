using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

public class AdditionalPostProcessingRenderFeature : ScriptableRendererFeature
{
    private AdditionalPostProcessingRenderPass additionalPostProcessingRenderPass;

    public override void Create()
    {
        additionalPostProcessingRenderPass = new AdditionalPostProcessingRenderPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer,
        in RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            return;
        additionalPostProcessingRenderPass.ConfigureInput(ScriptableRenderPassInput.Depth);
        additionalPostProcessingRenderPass.ConfigureInput(ScriptableRenderPassInput.Normal);
        additionalPostProcessingRenderPass.ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            return;

        renderer.EnqueuePass(additionalPostProcessingRenderPass);
    }

    protected override void Dispose(bool disposing)
    {
        additionalPostProcessingRenderPass.Dispose();
    }

    private class MaterialLibrary
    {
        public static readonly Material volumeFogMat =
            CoreUtils.CreateEngineMaterial("Addition/Post-processing/VolumetricCloud_");
    }

    private class AdditionalPostProcessingRenderPass : ScriptableRenderPass
    {
        private static readonly int boxMin = Shader.PropertyToID("_BoxMin");
        private static readonly int boxMax = Shader.PropertyToID("_BoxMax");
        private static readonly int density = Shader.PropertyToID("_Density");
        private static readonly int sigmaAbsorption = Shader.PropertyToID("_SigmaAbsorption");
        private static readonly int sigmaScattering = Shader.PropertyToID("_SigmaScattering");
        private static readonly int lightIntensity = Shader.PropertyToID("_LightIntensity");
        private static readonly int transmission = Shader.PropertyToID("_Transmission");
        private static readonly int reflection = Shader.PropertyToID("_Reflection");
        private static readonly int attenuation = Shader.PropertyToID("_Attenuation");
        private static readonly int contribution = Shader.PropertyToID("_Contribution");
        private static readonly int phaseAttenuation = Shader.PropertyToID("_PhaseAttenuation");
        private static readonly int exposure = Shader.PropertyToID("_Exposure");
        private static readonly int noiseTexture = Shader.PropertyToID("_NoiseTex");
        private static readonly int blueNoiseTexture = Shader.PropertyToID("_BlueNoiseTex");
        private static readonly int noiseScale = Shader.PropertyToID("_NoiseScale");
        private static readonly int noiseOffset = Shader.PropertyToID("_NoiseOffset");
        private static readonly int stepCount = Shader.PropertyToID("_StepCount");
        private static readonly int lightSampleCount = Shader.PropertyToID("_LightSampleCount");
        private static readonly int cloudBottom = Shader.PropertyToID("_CloudBottom");
        private static readonly int edgeFadeThreshold = Shader.PropertyToID("_EdgeFadeThreshold");
        
        private VolumetricCloud volumetricCloud;

        private RTHandle[] frameBufferHandles;
        private RenderTextureDescriptor backBufferDesc;
        private int currentFramebufferIndex;

        private bool enableOutline;
        private bool enableVolumeFog;
        private bool enableExponentialFog;

        private RTHandle CurrentFrameBufferHandle => frameBufferHandles[currentFramebufferIndex];
        private RTHandle CurrentBackBufferHandle => frameBufferHandles[(currentFramebufferIndex + 1) % 2];

        public AdditionalPostProcessingRenderPass()
        {
            profilingSampler = new ProfilingSampler("Additional Post Processing");
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            backBufferDesc = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.ARGBFloat, 0);
            frameBufferHandles = new RTHandle[2];
            currentFramebufferIndex = 0;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            backBufferDesc.width = cameraTextureDescriptor.width;
            backBufferDesc.height = cameraTextureDescriptor.height;

            RenderingUtils.ReAllocateIfNeeded(ref frameBufferHandles[0], backBufferDesc);
            RenderingUtils.ReAllocateIfNeeded(ref frameBufferHandles[1], backBufferDesc);
            ConfigureColorStoreAction(RenderBufferStoreAction.StoreAndResolve);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            CameraData cameraData = renderingData.cameraData;
            RTHandle cameraTargetHandle = cameraData.renderer.cameraColorTargetHandle;

            cmd.SetRenderTarget(CurrentFrameBufferHandle);
            Blitter.BlitTexture(cmd, cameraTargetHandle, new Vector4(1, 1, 0, 0), 0, false);
            currentFramebufferIndex = (currentFramebufferIndex + 1) % 2;

            var stack = VolumeManager.instance.stack;
            volumetricCloud = stack.GetComponent<VolumetricCloud>();

            enableVolumeFog = volumetricCloud.IsActive() && MaterialLibrary.volumeFogMat;

            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (enableVolumeFog)
                {
                    DoVolumeFog(cmd, CurrentFrameBufferHandle, CurrentBackBufferHandle, ref renderingData);
                    currentFramebufferIndex = (currentFramebufferIndex + 1) % 2;
                }

                DoFinalBlit(cmd, cameraTargetHandle, CurrentBackBufferHandle);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DoFinalBlit(CommandBuffer cmd, RTHandle cameraTargetHandle, RTHandle source)
        {
            cmd.SetRenderTarget(cameraTargetHandle);
            Blitter.BlitTexture(cmd, source, new Vector4(1, 1, 0, 0), 0, false);
        }

        private void DoVolumeFog(CommandBuffer cmd, RTHandle currentFrameBufferHandle, RTHandle currentBackBufferHandle,
            ref RenderingData renderingData)
        {
            var volumetricFogMat = MaterialLibrary.volumeFogMat;
            volumetricFogMat.SetVector(boxMin, volumetricCloud.boxMin.value);
            volumetricFogMat.SetVector(boxMax, volumetricCloud.boxMax.value);
            volumetricFogMat.SetFloat(density, volumetricCloud.density.value);
            volumetricFogMat.SetVector(sigmaAbsorption, volumetricCloud.sigmaAbsorption.value);
            volumetricFogMat.SetVector(sigmaScattering, volumetricCloud.sigmaScattering.value);
            volumetricFogMat.SetFloat(lightIntensity, volumetricCloud.lightIntensity.value);
            volumetricFogMat.SetFloat(transmission, volumetricCloud.transmission.value);
            volumetricFogMat.SetFloat(reflection, volumetricCloud.reflection.value);
            volumetricFogMat.SetFloat(attenuation, volumetricCloud.attenuation.value);
            volumetricFogMat.SetFloat(contribution, volumetricCloud.contribution.value);
            volumetricFogMat.SetFloat(phaseAttenuation, volumetricCloud.phaseAttenuation.value);
            volumetricFogMat.SetFloat(exposure, volumetricCloud.exposure.value);
            if (volumetricCloud.blueNoiseTexture.value)
                volumetricFogMat.SetTexture(blueNoiseTexture, volumetricCloud.blueNoiseTexture.value);
            if (volumetricCloud.noiseTexture.value)
            {
                volumetricFogMat.SetTexture(noiseTexture, volumetricCloud.noiseTexture.value);
                volumetricFogMat.SetVector(noiseScale, volumetricCloud.noiseScale.value);
                volumetricFogMat.SetVector(noiseOffset, volumetricCloud.noiseOffset.value);
            }
            
            volumetricFogMat.SetInt(stepCount, volumetricCloud.stepCount.value);
            volumetricFogMat.SetInt(lightSampleCount, volumetricCloud.lightSampleCount.value);
            volumetricFogMat.SetFloat(cloudBottom, volumetricCloud.cloudBottom.value);
            volumetricFogMat.SetFloat(edgeFadeThreshold, volumetricCloud.edgeFadeThreshold.value);

            cmd.SetRenderTarget(currentFrameBufferHandle);
            Blitter.BlitTexture(cmd, currentBackBufferHandle, new Vector4(1, 1, 0, 0), volumetricFogMat, 0);
        }

        public void Dispose()
        {
            if (enableVolumeFog && volumetricCloud.noiseTexture.value is RenderTexture rt)
            {
                rt.Release();
                volumetricCloud.noiseTexture.value = null;
            }
            frameBufferHandles[0]?.Release();
            frameBufferHandles[1]?.Release();
        }
    }
}