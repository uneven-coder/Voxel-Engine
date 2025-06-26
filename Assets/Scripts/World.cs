using System;
using System.Collections.Generic;
using UnityEngine;

public class World : MonoBehaviour
{
    public Vector2Int worldSize = new Vector2Int(5, 3);
    public int chunkSize = 16;
    private Dictionary<Vector3Int, Chunk> chunks;
    public static World Instance { get; private set; }
    public Material VoxelMaterial;
    public ComputeShader VoxelComputeShader;
    public int noiseSeed = 1234;
    public float maxHeight = 0.2f;
    public float noiseScale = 0.015f;
    public float[,] noiseArray;
    private Vector3Int worldOrigin;

    private void Awake()
    {   // Initialize singleton instance and world origin
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
        noiseArray = GlobalNoise.GetNoise();
        int centerX = worldSize.x / 2;
        int centerY = worldSize.y / 2;
        int centerZ = worldSize.x / 2;
        Vector3 worldPos = transform.position;
        worldOrigin = new Vector3Int(
            Mathf.RoundToInt(worldPos.x) - centerX * chunkSize,
            Mathf.RoundToInt(worldPos.y) - centerY * chunkSize,
            Mathf.RoundToInt(worldPos.z) - centerZ * chunkSize
        );
    }

    private void Start()
    {   // Initialize world and load all chunks
        chunks = new Dictionary<Vector3Int, Chunk>(worldSize.x * worldSize.x * worldSize.y);
        if (VoxelComputeShader != null)
            ComputeShaderVoxelManager.Initialize(VoxelComputeShader);
        LoadAllChunks();
    }

    private void LoadAllChunks()
    {   // Load chunks near the camera, or all if camera not found
        Vector3 camPos = Camera.main ? Camera.main.transform.position : transform.position;
        int renderDistance = 1000; // Large value to ensure all chunks are loaded
        bool cameraFound = Camera.main != null;
        for (int x = 0; x < worldSize.x; x++)
        for (int y = 0; y < worldSize.y; y++)
        for (int z = 0; z < worldSize.x; z++)
        {
            Vector3Int chunkCoord = new Vector3Int(x, y, z);
            Vector3 chunkWorldPos = ChunkCoordToWorldPos(chunkCoord);
            float dist = Vector3.Distance(chunkWorldPos, camPos);
            if (!cameraFound || dist < renderDistance * chunkSize)
                if (!chunks.ContainsKey(chunkCoord))
                    LoadChunk(chunkCoord);
        }
    }

    private void LoadChunk(Vector3Int chunkCoord)
    {   // Create and initialize a new chunk at the specified coordinates using compute shader
        Vector3 chunkWorldPos = ChunkCoordToWorldPos(chunkCoord);
        GameObject chunkObj = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}");
        chunkObj.transform.position = chunkWorldPos;
        chunkObj.transform.parent = transform;
        var initialVoxelData = GenerateChunkVoxelData(chunkCoord);
        Chunk chunk = chunkObj.AddComponent<Chunk>();
        chunk.Initialize(chunkSize, VoxelComputeShader, chunkCoord, initialVoxelData);
        chunks[chunkCoord] = chunk;
    }

    private ComputeShaderVoxelManager.VoxelData[] GenerateChunkVoxelData(Vector3Int chunkCoord)
    {   // Generate layered voxel data for a specific chunk (stone, orange, pink, grass, air)
        var voxels = new ComputeShaderVoxelManager.VoxelData[chunkSize * chunkSize * chunkSize];
        Vector3 chunkWorldPos = ChunkCoordToWorldPos(chunkCoord);
        for (int x = 0; x < chunkSize; x++)
        for (int y = 0; y < chunkSize; y++)
        for (int z = 0; z < chunkSize; z++)
        {
            float wx = chunkWorldPos.x + x;
            float wy = chunkWorldPos.y + y;
            float wz = chunkWorldPos.z + z;
            Voxel.VoxelType type;
            if (y < chunkSize * 0.3f)
                type = Voxel.VoxelType.Stone;
            else if (y < chunkSize * 0.6f)
                type = Voxel.VoxelType.Orange;
            else if (y < chunkSize * 0.8f)
                type = Voxel.VoxelType.Pink;
            else if (y < chunkSize * 0.95f)
                type = Voxel.VoxelType.Grass;
            else
                type = Voxel.VoxelType.Air;
            Color color = VoxelTypeManager.GetVoxelColor(type);
            int index = x + y * chunkSize + z * chunkSize * chunkSize;
            voxels[index] = new ComputeShaderVoxelManager.VoxelData
            {
                position = new Vector3(x, y, z),
                color = color,
                type = (int)type,
                isActive = type != Voxel.VoxelType.Air ? 1 : 0
            };
        }
        return voxels;
    }

    private Vector3Int ChunkCoordToWorldPos(Vector3Int chunkCoord) =>
        worldOrigin + new Vector3Int(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);

    public Voxel.VoxelType DetermineVoxelType(float x, float y, float z)
    {   // Determine voxel type based on terrain height and noise
        int cSize = chunkSize;
        float noiseValue = GlobalNoise.GetGlobalNoiseValue(x, z, noiseArray);
        float normalizedNoiseValue = (noiseValue + 1) * 0.5f;
        float terrainHeight = normalizedNoiseValue * maxHeight * cSize;
        return y <= terrainHeight ? Voxel.VoxelType.Grass : Voxel.VoxelType.Air;
    }

    public Chunk GetChunk(Vector3 globalPosition)
    {   // Retrieve chunk from world position if loaded
        Vector3Int chunkCoord = GetChunkCoord(globalPosition);
        if (chunks.TryGetValue(chunkCoord, out Chunk chunk))
            return chunk;
        return null;
    }

    public Vector3Int GetChunkCoord(Vector3 position)
    {   // Calculate chunk coordinates from world position
        int x = Mathf.FloorToInt((position.x - worldOrigin.x) / chunkSize);
        int y = Mathf.FloorToInt((position.y - worldOrigin.y) / chunkSize);
        int z = Mathf.FloorToInt((position.z - worldOrigin.z) / chunkSize);
        return new Vector3Int(x, y, z);
    }
}