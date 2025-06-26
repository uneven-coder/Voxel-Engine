using System;
using System.Collections.Generic;
using UnityEngine;

public static class ComputeShaderVoxelManager
{
    private static ComputeShader voxelComputeShader;
    private static Dictionary<Vector3Int, ChunkBuffers> chunkBuffers = new Dictionary<Vector3Int, ChunkBuffers>();

    private class ChunkBuffers
    {   // Holds all buffers for a chunk
        public ComputeBuffer voxelBuffer;
        public ComputeBuffer faceBuffer;
        public ComputeBuffer destructionBuffer;
        public ComputeBuffer visibilityBuffer;
        public ComputeBuffer faceCountBuffer;
        public int chunkSize;
    }

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
        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;
        public Vector3 v3;
        public Vector2 uv0;
        public Vector2 uv1;
        public Vector2 uv2;
        public Vector2 uv3;
        public int materialIndex;
        public int isVisible;
    }
    
    public static void Initialize(ComputeShader computeShader)
    {   // Initialize compute shader and find kernel indices
        voxelComputeShader = computeShader;

        if (voxelComputeShader != null)
        {   // Check for required kernels and log available/missing ones
            string[] requiredKernels = { "FaceCulling", "VoxelDestruction", "MeshGeneration" };
            List<string> foundKernels = new List<string>();
            List<string> missingKernels = new List<string>();
            foreach (var kernel in requiredKernels)
            {
                if (voxelComputeShader.HasKernel(kernel))
                    foundKernels.Add(kernel);
                else
                    missingKernels.Add(kernel);
            }
            faceCullingKernel = voxelComputeShader.HasKernel("FaceCulling") ? voxelComputeShader.FindKernel("FaceCulling") : -1;
            voxelDestructionKernel = voxelComputeShader.HasKernel("VoxelDestruction") ? voxelComputeShader.FindKernel("VoxelDestruction") : -1;
            meshGenerationKernel = voxelComputeShader.HasKernel("MeshGeneration") ? voxelComputeShader.FindKernel("MeshGeneration") : -1;
        }
    }
    
    public static void RegisterChunkData(Vector3Int chunkCoord, int chunkSize, VoxelData[] data)
    {   // Register and upload voxel data for a chunk
        if (chunkBuffers.ContainsKey(chunkCoord))
            DisposeChunkBuffers(chunkCoord);
        var buffers = new ChunkBuffers();
        buffers.chunkSize = chunkSize;
        int voxelCount = chunkSize * chunkSize * chunkSize;
        int faceCount = voxelCount * 6;
        buffers.voxelBuffer = new ComputeBuffer(voxelCount, sizeof(float) * 3 + sizeof(float) * 4 + sizeof(int) * 2);
        buffers.faceBuffer = new ComputeBuffer(faceCount, sizeof(float) * 3 * 4 + sizeof(float) * 2 * 4 + sizeof(int) * 2);
        buffers.destructionBuffer = new ComputeBuffer(voxelCount, sizeof(int));
        buffers.visibilityBuffer = new ComputeBuffer(faceCount, sizeof(int));
        buffers.faceCountBuffer = new ComputeBuffer(chunkSize * chunkSize, sizeof(int));
        buffers.voxelBuffer.SetData(data);
        chunkBuffers[chunkCoord] = buffers;
    }

    public static void PerformFaceCulling(Vector3Int chunkCoord, int chunkSize, Vector3 cameraPosition)
    {   // Run face culling for a chunk
        if (!chunkBuffers.ContainsKey(chunkCoord))
            return;
        if (voxelComputeShader == null)
        {   // ComputeShader asset is missing
            Debug.LogError("Cannot dispatch FaceCulling: ComputeShader is null. Please assign the asset in the inspector.");
            return;
        }
        if (!voxelComputeShader.HasKernel("FaceCulling"))
        {   // Kernel not found at runtime
            Debug.LogError("Cannot dispatch FaceCulling: ComputeShader does not have kernel 'FaceCulling' at runtime. Check asset import and compilation.");
            return;
        }
        if (faceCullingKernel < 0)
        {   // Kernel not found, log error and skip dispatch
            Debug.LogError($"Cannot dispatch FaceCulling: kernel index is invalid ({faceCullingKernel}). Check that the kernel exists in the ComputeShader asset.");
            return;
        }
        var buffers = chunkBuffers[chunkCoord];
        if (buffers.voxelBuffer == null || buffers.faceBuffer == null || buffers.visibilityBuffer == null)
        {   // Log buffer status if any are null
            Debug.LogError($"FaceCulling dispatch failed: One or more buffers are null. voxelBuffer null: {buffers.voxelBuffer == null}, faceBuffer null: {buffers.faceBuffer == null}, visibilityBuffer null: {buffers.visibilityBuffer == null}");
            return;
        }
        if (chunkSize <= 0)
        {   // Log chunk size if invalid
            Debug.LogError($"FaceCulling dispatch failed: chunkSize is {chunkSize}");
            return;
        }
        int threadGroups = Mathf.CeilToInt(chunkSize / 8.0f);
        Debug.Log($"Dispatching FaceCulling: chunkSize={chunkSize}, threadGroups={threadGroups}, buffers OK, faceCullingKernel={faceCullingKernel}, computeShader name={(voxelComputeShader != null ? voxelComputeShader.name : "null")}");
        voxelComputeShader.SetBuffer(faceCullingKernel, VoxelBufferID, buffers.voxelBuffer);
        voxelComputeShader.SetBuffer(faceCullingKernel, FaceBufferID, buffers.faceBuffer);
        voxelComputeShader.SetBuffer(faceCullingKernel, VisibilityBufferID, buffers.visibilityBuffer);
        voxelComputeShader.SetInt(ChunkSizeID, chunkSize);
        voxelComputeShader.SetVector(CameraPositionID, cameraPosition);
        voxelComputeShader.Dispatch(faceCullingKernel, threadGroups, threadGroups, 1);
    }

    public static FaceData[] GetFaceData(Vector3Int chunkCoord, int chunkSize)
    {   // Get face data for a chunk
        if (!chunkBuffers.ContainsKey(chunkCoord))
            return null;
        var buffers = chunkBuffers[chunkCoord];
        int faceCount = chunkSize * chunkSize * chunkSize * 6;
        FaceData[] faceData = new FaceData[faceCount];
        buffers.faceBuffer.GetData(faceData);
        return faceData;
    }

    public static int[] GetVisibilityData(Vector3Int chunkCoord, int chunkSize)
    {   // Get visibility data for a chunk
        if (!chunkBuffers.ContainsKey(chunkCoord))
            return null;
        var buffers = chunkBuffers[chunkCoord];
        int faceCount = chunkSize * chunkSize * chunkSize * 6;
        int[] visibilityData = new int[faceCount];
        buffers.visibilityBuffer.GetData(visibilityData);
        return visibilityData;
    }

    public static void PerformVoxelDestruction(Vector3Int chunkCoord, int chunkSize, Vector3 destructionCenter, int destructionRadius, int[] destructionIndices)
    {   // Destroy voxels in a chunk
        if (!chunkBuffers.ContainsKey(chunkCoord))
            return;
        if (voxelDestructionKernel == -1)
        {   // Kernel not found, log error and skip dispatch
            Debug.LogError("Cannot dispatch VoxelDestruction: kernel index is invalid.");
            return;
        }
        var buffers = chunkBuffers[chunkCoord];
        buffers.destructionBuffer.SetData(destructionIndices);
        voxelComputeShader.SetBuffer(voxelDestructionKernel, VoxelBufferID, buffers.voxelBuffer);
        voxelComputeShader.SetBuffer(voxelDestructionKernel, DestructionBufferID, buffers.destructionBuffer);
        voxelComputeShader.SetInt(ChunkSizeID, chunkSize);
        voxelComputeShader.SetVector(DestructionCenterID, destructionCenter);
        voxelComputeShader.SetInt(DestructionRadiusID, destructionRadius);
        voxelComputeShader.SetInt(DestructionCountID, destructionIndices.Length);
        int threadGroups = Mathf.CeilToInt(destructionIndices.Length / 64.0f);
        voxelComputeShader.Dispatch(voxelDestructionKernel, threadGroups, 1, 1);
    }

    public static void DisposeChunkBuffers(Vector3Int chunkCoord)
    {   // Dispose all buffers for a chunk
        if (!chunkBuffers.ContainsKey(chunkCoord))
            return;
        var buffers = chunkBuffers[chunkCoord];
        buffers.voxelBuffer?.Dispose();
        buffers.faceBuffer?.Dispose();
        buffers.destructionBuffer?.Dispose();
        buffers.visibilityBuffer?.Dispose();
        buffers.faceCountBuffer?.Dispose();
        chunkBuffers.Remove(chunkCoord);
    }

    public static void DisposeBuffers()
    {   // Clean up compute buffers to prevent memory leaks
        foreach (var chunk in chunkBuffers.Keys)
            DisposeChunkBuffers(chunk);
    }
    
    public static bool IsInitialized() =>
        voxelComputeShader != null;
}