using System;
using UnityEngine;

public static class ComputeShaderVoxelManager
{
    private static ComputeShader voxelComputeShader;
    private static ComputeBuffer voxelBuffer;
    private static ComputeBuffer faceBuffer;
    private static ComputeBuffer destructionBuffer;
    private static ComputeBuffer visibilityBuffer;
    private static ComputeBuffer faceCountBuffer;
    
    private static int faceCullingKernel;
    private static int voxelDestructionKernel;
    private static int meshGenerationKernel;
    
    // Cached property IDs for better performance
    private static readonly int VoxelBufferID = Shader.PropertyToID("voxelBuffer");
    private static readonly int FaceBufferID = Shader.PropertyToID("faceBuffer");
    private static readonly int DestructionBufferID = Shader.PropertyToID("destructionBuffer");
    private static readonly int VisibilityBufferID = Shader.PropertyToID("visibilityBuffer");
    private static readonly int FaceCountBufferID = Shader.PropertyToID("faceCountBuffer");
    private static readonly int ChunkSizeID = Shader.PropertyToID("chunkSize");
    private static readonly int DestructionRadiusID = Shader.PropertyToID("destructionRadius");
    private static readonly int DestructionCenterID = Shader.PropertyToID("destructionCenter");
    private static readonly int DestructionCountID = Shader.PropertyToID("destructionCount");
    private static readonly int CameraPositionID = Shader.PropertyToID("cameraPosition");
    
    public struct VoxelData
    {
        public Vector3 position;
        public Color color;
        public int type;
        public int isActive;
    }
    
    public struct FaceData
    {
        public Vector3[] vertices;
        public Vector2[] uvs;
        public int materialIndex;
        public int isVisible;
        
        public FaceData(bool initialize)
        {   // Initialize face data structure with default values
            vertices = new Vector3[4];
            uvs = new Vector2[4];
            materialIndex = 0;
            isVisible = 0;
        }
    }
    
    public static void Initialize(ComputeShader computeShader)
    {   // Initialize compute shader and find kernel indices
        voxelComputeShader = computeShader;
        
        if (voxelComputeShader != null)
        {   // Cache kernel indices for performance
            faceCullingKernel = voxelComputeShader.FindKernel("FaceCulling");
            voxelDestructionKernel = voxelComputeShader.FindKernel("VoxelDestruction");
            meshGenerationKernel = voxelComputeShader.FindKernel("MeshGeneration");
        }
        else
            Debug.LogError("VoxelComputeShader not found! Please assign the compute shader.");
    }
    
    public static void InitializeBuffers(int chunkSize)
    {   // Create compute buffers for voxel processing
        int voxelCount = chunkSize * chunkSize * chunkSize;
        int faceCount = voxelCount * 6;
        
        // Create buffers with appropriate sizes
        voxelBuffer = new ComputeBuffer(voxelCount, sizeof(float) * 3 + sizeof(float) * 4 + sizeof(int) * 2);
        faceBuffer = new ComputeBuffer(faceCount, sizeof(float) * 3 * 4 + sizeof(float) * 2 * 4 + sizeof(int) * 2);
        destructionBuffer = new ComputeBuffer(voxelCount, sizeof(int));
        visibilityBuffer = new ComputeBuffer(faceCount, sizeof(int));
        faceCountBuffer = new ComputeBuffer(chunkSize * chunkSize, sizeof(int));
    }
    
    public static void SetVoxelData(VoxelData[] voxelData)
    {   // Upload voxel data to GPU buffer
        if (voxelBuffer != null && voxelData != null)
            voxelBuffer.SetData(voxelData);
    }
    
    public static void PerformFaceCulling(int chunkSize, Vector3 cameraPosition)
    {   // Execute face culling compute shader
        if (voxelComputeShader == null || voxelBuffer == null)
            return;
            
        // Set compute shader parameters
        voxelComputeShader.SetBuffer(faceCullingKernel, VoxelBufferID, voxelBuffer);
        voxelComputeShader.SetBuffer(faceCullingKernel, FaceBufferID, faceBuffer);
        voxelComputeShader.SetBuffer(faceCullingKernel, VisibilityBufferID, visibilityBuffer);
        voxelComputeShader.SetInt(ChunkSizeID, chunkSize);
        voxelComputeShader.SetVector(CameraPositionID, cameraPosition);
        
        // Dispatch compute shader with appropriate thread groups
        int threadGroups = Mathf.CeilToInt(chunkSize / 8.0f);
        voxelComputeShader.Dispatch(faceCullingKernel, threadGroups, threadGroups, 1);
    }
    
    public static void PerformVoxelDestruction(int chunkSize, Vector3 destructionCenter, int destructionRadius, int[] destructionIndices)
    {   // Execute voxel destruction compute shader
        if (voxelComputeShader == null || voxelBuffer == null || destructionIndices == null)
            return;
            
        // Upload destruction indices
        destructionBuffer.SetData(destructionIndices);
        
        // Set compute shader parameters
        voxelComputeShader.SetBuffer(voxelDestructionKernel, VoxelBufferID, voxelBuffer);
        voxelComputeShader.SetBuffer(voxelDestructionKernel, DestructionBufferID, destructionBuffer);
        voxelComputeShader.SetInt(ChunkSizeID, chunkSize);
        voxelComputeShader.SetVector(DestructionCenterID, destructionCenter);
        voxelComputeShader.SetInt(DestructionRadiusID, destructionRadius);
        voxelComputeShader.SetInt(DestructionCountID, destructionIndices.Length);
        
        // Dispatch with thread count matching destruction count
        int threadGroups = Mathf.CeilToInt(destructionIndices.Length / 64.0f);
        voxelComputeShader.Dispatch(voxelDestructionKernel, threadGroups, 1, 1);
    }
    
    public static VoxelData[] GetVoxelData(int chunkSize)
    {   // Retrieve updated voxel data from GPU
        if (voxelBuffer == null)
            return null;
            
        int voxelCount = chunkSize * chunkSize * chunkSize;
        VoxelData[] voxelData = new VoxelData[voxelCount];
        voxelBuffer.GetData(voxelData);
        return voxelData;
    }
    
    public static int[] GetVisibilityData(int chunkSize)
    {   // Retrieve face visibility data from GPU
        if (visibilityBuffer == null)
            return null;
            
        int faceCount = chunkSize * chunkSize * chunkSize * 6;
        int[] visibilityData = new int[faceCount];
        visibilityBuffer.GetData(visibilityData);
        return visibilityData;
    }
    
    public static FaceData[] GetFaceData(int chunkSize)
    {   // Retrieve face geometry data from GPU
        if (faceBuffer == null)
            return null;
            
        int faceCount = chunkSize * chunkSize * chunkSize * 6;
        FaceData[] faceData = new FaceData[faceCount];
        faceBuffer.GetData(faceData);
        return faceData;
    }
    
    public static void DisposeBuffers()
    {   // Clean up compute buffers to prevent memory leaks
        voxelBuffer?.Dispose();
        faceBuffer?.Dispose();
        destructionBuffer?.Dispose();
        visibilityBuffer?.Dispose();
        faceCountBuffer?.Dispose();
        
        voxelBuffer = null;
        faceBuffer = null;
        destructionBuffer = null;
        visibilityBuffer = null;
        faceCountBuffer = null;
    }
    
    public static bool IsInitialized() =>
        voxelComputeShader != null && voxelBuffer != null;
}