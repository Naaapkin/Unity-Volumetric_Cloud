using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

[Serializable, VolumeComponentMenu("Addition-post-processing/VolumetricCloud")]
public class VolumetricCloud : VolumeComponent, IPostProcessComponent
{
    public Vector3Parameter boxMin = new Vector3Parameter(Vector3.one * -10);
    public Vector3Parameter boxMax = new Vector3Parameter(Vector3.one * 10);
    public FloatParameter density = new FloatParameter(0); 
    public Vector3Parameter sigmaAbsorption = new Vector3Parameter(Vector3.zero);
    public Vector3Parameter sigmaScattering = new Vector3Parameter(Vector3.one);
    public ClampedFloatParameter transmission = new ClampedFloatParameter(0.8f, 0, 1);
    public ClampedFloatParameter reflection = new ClampedFloatParameter(0.5f, 0, 1);
    public ClampedFloatParameter attenuation = new ClampedFloatParameter(0.5f, 0, 1);
    public ClampedFloatParameter contribution = new ClampedFloatParameter(0.2f, 0, 1);
    public ClampedFloatParameter phaseAttenuation = new ClampedFloatParameter(0.1f, 0, 1);
    public FloatParameter exposure = new FloatParameter(150);
    public Texture2DParameter blueNoiseTexture = new Texture2DParameter(null);
    public Texture3DParameter noiseTexture = new Texture3DParameter(null);
    public Vector3Parameter noiseScale = new Vector3Parameter(Vector3.one);
    public Vector3Parameter noiseOffset = new Vector3Parameter(Vector3.zero);
    public ClampedIntParameter stepCount = new ClampedIntParameter(32, 16, 128);
    public ClampedIntParameter lightSampleCount = new ClampedIntParameter(4, 8, 16);
    public FloatParameter cloudBottom = new FloatParameter(100);
    public ClampedFloatParameter edgeFadeThreshold = new ClampedFloatParameter(50.0f, 1.0f, 500.0f);
    
    public bool IsActive()
    {
        return density.value > 1e-6f && stepCount.value > 0;
    }

    public bool IsTileCompatible()
    {
        return false;
    }
}
