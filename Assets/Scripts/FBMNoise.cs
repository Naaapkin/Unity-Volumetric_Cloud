using System;
using UnityEngine;
using UnityEngine.Rendering;

public class FBMNoise
{
    public struct FBMParameters
    {
        public int seed;
        public float frequency;
        public int octaves;
        public float lacunarity;
        public float persistence;
    } 
    
    private static readonly int P_SIZE = Shader.PropertyToID("Size");
    private static readonly int P_SEED = Shader.PropertyToID("Seed");
    private static readonly int P_FREQUENCY = Shader.PropertyToID("Frequency");
    private static readonly int P_OCTAVES = Shader.PropertyToID("Octaves");
    private static readonly int P_LACUNARITY = Shader.PropertyToID("Lacunarity");
    private static readonly int P_PERSISTENCE = Shader.PropertyToID("Persistence");
    private static readonly int P_WRAP = Shader.PropertyToID("Wrap");
    private static readonly int P_NOISE2D = Shader.PropertyToID("Noise2D");
    private static readonly int P_NOISE3D = Shader.PropertyToID("Noise3D");
    
    private readonly ComputeShader noiseShader;
    
    public static Texture CreateTexture(int width, int height, int depth, TextureFormat format, TextureDimension d)
    {
        Texture tex = d switch
        {
            TextureDimension.Tex2D => new Texture2D(width, height, format, false, true)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 6
            },
            TextureDimension.Tex3D => new Texture3D(width, height, depth, format, false)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 6
            },
            _ => null
        };
        return tex;
    }
    
    public FBMNoise(ComputeShader shader)
    {
        noiseShader = shader;
        K_PERLIN_NOISE_2D = shader.FindKernel("PerlinNoise2D");
        K_PERLIN_NOISE_3D = shader.FindKernel("PerlinNoise3D");
        K_WORLEY_NOISE_2D = shader.FindKernel("WorleyNoise2D");
        K_WORLEY_NOISE_3D = shader.FindKernel("WorleyNoise3D");
        K_PERLIN_WORLEY_NOISE_2D = shader.FindKernel("PerlinWorley2D");
        K_PERLIN_WORLEY_NOISE_3D = shader.FindKernel("PerlinWorley3D");
        K_SIMPLEX_NOISE_2D = shader.FindKernel("SimplexNoise2D");
    }
    
    public Texture2D Perlin2D(Vector2Int size, FBMParameters parameters, bool wrap)
    {
        if (size.x == 0 || size.y == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, 0, RenderTextureFormat.RFloat, TextureDimension.Tex2D);
        
        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        noiseShader.SetTexture(K_PERLIN_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(ref parameters, size.x, size.y);
        noiseShader.SetBool(P_WRAP, wrap);
        noiseShader.Dispatch(K_PERLIN_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
        
        Texture2D tex = CreateTexture(size.x, size.y, 0, TextureFormat.RFloat, TextureDimension.Tex2D) as Texture2D;
        Utility.ReadRT2D(noise, tex);
        return tex;
    }
    
    public void Perlin2D(CommandBuffer cmd, RenderTexture noise, FBMParameters parameters, bool wrap)
    {       
        Vector2Int size = new Vector2Int(noise.width, noise.height); 
        if (size.x == 0 || size.y == 0) return;
        
        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        cmd.SetComputeTextureParam(noiseShader, K_PERLIN_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(cmd, ref parameters, size.x, size.y);
        cmd.SetComputeIntParam(noiseShader, P_WRAP, wrap ? 1 : 0);
        cmd.DispatchCompute(noiseShader, K_PERLIN_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
    }
    
    public Texture3D Perlin3D(Vector3Int size, FBMParameters parameters, bool wrap)
    {
        if (size.x == 0 || size.y == 0 || size.z == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, size.z, RenderTextureFormat.RFloat, TextureDimension.Tex3D);
        
        parameters.frequency *= 4.0f / Mathf.Max(size.z, Mathf.Min(size.x, size.y));
        noiseShader.SetTexture(K_PERLIN_NOISE_3D, P_NOISE3D, noise);
        SetFBMParameters(ref parameters, size.x, size.y, size.z);
        noiseShader.SetBool(P_WRAP, wrap);
        noiseShader.Dispatch(K_PERLIN_NOISE_3D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), size.z);
        
        Texture3D tex = CreateTexture(size.x, size.y, size.z, TextureFormat.RFloat, TextureDimension.Tex3D) as Texture3D;
        Utility.ReadRT3D(noise, tex, 4);
        return tex;
    }
    
    public void Perlin3D(CommandBuffer cmd, RenderTexture noise, FBMParameters parameters, bool wrap)
    {       
        Vector3Int size = new Vector3Int(noise.width, noise.height, noise.volumeDepth); 
        if (size.x == 0 || size.y == 0 || size.z == 0) return;
        
        parameters.frequency *= 4.0f / Mathf.Max(size.z, Mathf.Min(size.x, size.y));
        cmd.SetComputeTextureParam(noiseShader, K_PERLIN_NOISE_3D, P_NOISE3D, noise);
        SetFBMParameters(cmd, ref parameters, size.x, size.y, size.z);
        cmd.SetComputeIntParam(noiseShader, P_WRAP, wrap ? 1 : 0);
        cmd.DispatchCompute(noiseShader, K_PERLIN_NOISE_3D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), size.z);
    }
    
    public Texture2D Worley2D(Vector2Int size, FBMParameters parameters, bool wrap)
    {
        if (size.x == 0 || size.y == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, 0, RenderTextureFormat.RFloat, TextureDimension.Tex2D);

        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        noiseShader.SetTexture(K_WORLEY_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(ref parameters, size.x, size.y);
        noiseShader.SetBool(P_WRAP, wrap);
        noiseShader.Dispatch(K_WORLEY_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
        
        Texture2D tex = CreateTexture(size.x, size.y, 0, TextureFormat.RFloat, TextureDimension.Tex2D) as Texture2D;
        Utility.ReadRT2D(noise, tex);
        return tex;
    }
    
    public void Worley2D(CommandBuffer cmd, RenderTexture noise, FBMParameters parameters, bool wrap)
    {       
        Vector2Int size = new Vector2Int(noise.width, noise.height); 
        if (size.x == 0 || size.y == 0) return;
        
        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        cmd.SetComputeTextureParam(noiseShader, K_WORLEY_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(cmd, ref parameters, size.x, size.y);
        cmd.SetComputeIntParam(noiseShader, P_WRAP, wrap ? 1 : 0);
        cmd.DispatchCompute(noiseShader, K_WORLEY_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
    }
    
    public Texture3D Worley3D(Vector3Int size, FBMParameters parameters, bool wrap)
    {
        if (size.x == 0 || size.y == 0 || size.z == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, size.z, RenderTextureFormat.RFloat, TextureDimension.Tex3D);
        
        parameters.frequency *= 4.0f / Mathf.Max(size.z, Mathf.Min(size.x, size.y));
        noiseShader.SetTexture(K_WORLEY_NOISE_3D, P_NOISE3D, noise);
        SetFBMParameters(ref parameters, size.x, size.y, size.z);
        noiseShader.SetBool(P_WRAP, wrap);
        noiseShader.Dispatch(K_WORLEY_NOISE_3D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), size.z);
        
        Texture3D tex = CreateTexture(size.x, size.y, size.z, TextureFormat.RFloat, TextureDimension.Tex3D) as Texture3D;
        Utility.ReadRT3D(noise, tex, 4);
        return tex;
    }
    
    public void Worley3D(CommandBuffer cmd, RenderTexture noise, FBMParameters parameters, bool wrap)
    {       
        Vector3Int size = new Vector3Int(noise.width, noise.height, noise.volumeDepth); 
        if (size.x == 0 || size.y == 0 || size.z == 0) return;

        parameters.frequency *= 4.0f / Mathf.Max(size.z, Mathf.Min(size.x, size.y));
        cmd.SetComputeTextureParam(noiseShader, K_WORLEY_NOISE_3D, P_NOISE3D, noise);
        SetFBMParameters(cmd, ref parameters, size.x, size.y, size.z);
        cmd.SetComputeIntParam(noiseShader, P_WRAP, wrap ? 1 : 0);
        cmd.DispatchCompute(noiseShader, K_WORLEY_NOISE_3D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), size.z);
    }
    
    public Texture2D PerlinWorley2D(Vector2Int size, FBMParameters parameters, bool wrap)
    {
        if (size.x == 0 || size.y == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, 0, RenderTextureFormat.RFloat, TextureDimension.Tex2D);

        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        noiseShader.SetTexture(K_PERLIN_WORLEY_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(ref parameters, size.x, size.y);
        noiseShader.SetBool(P_WRAP, wrap);
        noiseShader.Dispatch(K_PERLIN_WORLEY_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
        
        Texture2D tex = CreateTexture(size.x, size.y, 0, TextureFormat.RFloat, TextureDimension.Tex2D) as Texture2D;
        Utility.ReadRT2D(noise, tex);
        return tex;
    }
    
    public void PerlinWorley2D(CommandBuffer cmd, RenderTexture noise, FBMParameters parameters, bool wrap)
    {       
        Vector2Int size = new Vector2Int(noise.width, noise.height); 
        if (size.x == 0 || size.y == 0) return;
        
        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        cmd.SetComputeTextureParam(noiseShader, K_PERLIN_WORLEY_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(cmd, ref parameters, size.x, size.y);
        cmd.SetComputeIntParam(noiseShader, P_WRAP, wrap ? 1 : 0);
        cmd.DispatchCompute(noiseShader, K_PERLIN_WORLEY_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
    }
    
    public Texture3D PerlinWorley3D(Vector3Int size, FBMParameters parameters, bool wrap)
    {
        if (size.x == 0 || size.y == 0 || size.z == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, size.z, RenderTextureFormat.RFloat, TextureDimension.Tex3D);
        
        parameters.frequency *= 4.0f / Mathf.Max(size.z, Mathf.Min(size.x, size.y));
        noiseShader.SetTexture(K_PERLIN_WORLEY_NOISE_3D, P_NOISE3D, noise);
        SetFBMParameters(ref parameters, size.x, size.y, size.z);
        noiseShader.SetBool(P_WRAP, wrap);
        noiseShader.Dispatch(K_PERLIN_WORLEY_NOISE_3D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), size.z);
        
        Texture3D tex = CreateTexture(size.x, size.y, size.z, TextureFormat.RFloat, TextureDimension.Tex3D) as Texture3D;
        Utility.ReadRT3D(noise, tex, 4);
        return tex;
    }
    
    public void PerlinWorley3D(CommandBuffer cmd, RenderTexture noise, FBMParameters parameters, bool wrap)
    {       
        Vector3Int size = new Vector3Int(noise.width, noise.height, noise.volumeDepth); 
        if (size.x == 0 || size.y == 0 || size.z == 0) return;

        parameters.frequency *= 4.0f / Mathf.Max(size.z, Mathf.Min(size.x, size.y));
        cmd.SetComputeTextureParam(noiseShader, K_PERLIN_WORLEY_NOISE_3D, P_NOISE3D, noise);
        SetFBMParameters(cmd, ref parameters, size.x, size.y, size.z);
        cmd.SetComputeIntParam(noiseShader, P_WRAP, wrap ? 1 : 0);
        cmd.DispatchCompute(noiseShader, K_PERLIN_WORLEY_NOISE_3D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), size.z);
    }
    
    public Texture2D Simplex2D(Vector3Int size, FBMParameters parameters)
    {
        if (size.x == 0 || size.y == 0) return null;
        RenderTexture noise = Utility.CreateRenderTexture(size.x, size.y, 0, RenderTextureFormat.RFloat, TextureDimension.Tex2D);

        parameters.frequency *= 4.0f / Mathf.Min(size.x, size.y);
        noiseShader.SetTexture(K_SIMPLEX_NOISE_2D, P_NOISE2D, noise);
        SetFBMParameters(ref parameters, size.x, size.y);
        noiseShader.Dispatch(K_SIMPLEX_NOISE_2D, Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3), 1);
        
        Texture2D tex = CreateTexture(size.x, size.y, 0, TextureFormat.RFloat, TextureDimension.Tex2D) as Texture2D;
        Utility.ReadRT2D(noise, tex);
        return tex;
    }
    
    private void SetFBMParameters(ref FBMParameters parameters, params int[] size)
    {
        noiseShader.SetInts(P_SIZE, size);
        noiseShader.SetInt(P_SEED, parameters.seed);
        noiseShader.SetFloat(P_FREQUENCY, parameters.frequency);
        noiseShader.SetInt(P_OCTAVES, parameters.octaves);
        noiseShader.SetFloat(P_LACUNARITY, parameters.lacunarity);
        noiseShader.SetFloat(P_PERSISTENCE, parameters.persistence);
    }
    
    private void SetFBMParameters(CommandBuffer cmd, ref FBMParameters parameters, params int[] size)
    {
        cmd.SetComputeIntParams(noiseShader, P_SIZE, size);
        cmd.SetComputeIntParam(noiseShader, P_SEED, (int) parameters.seed);
        cmd.SetComputeFloatParam(noiseShader, P_FREQUENCY, parameters.frequency);
        cmd.SetComputeIntParam(noiseShader, P_OCTAVES, parameters.octaves);
        cmd.SetComputeFloatParam(noiseShader, P_LACUNARITY, parameters.lacunarity);
        cmd.SetComputeFloatParam(noiseShader, P_PERSISTENCE, parameters.persistence);
    }
    
    private readonly int K_PERLIN_NOISE_2D;
    private readonly int K_PERLIN_NOISE_3D;
    private readonly int K_WORLEY_NOISE_2D;
    private readonly int K_WORLEY_NOISE_3D;
    private readonly int K_PERLIN_WORLEY_NOISE_2D;
    private readonly int K_PERLIN_WORLEY_NOISE_3D;
    private readonly int K_SIMPLEX_NOISE_2D;
}