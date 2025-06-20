using System;
using System.Collections.Generic;
using UnityEngine;

public class World : MonoBehaviour
{
    public Vector2Int worldSize = new Vector2Int(5, 3); // World size in chunks (XZ, Y)
    public int chunkSize = 16; // Size of each chunk

    private Dictionary<Vector3, Chunk> chunks;

    public static World Instance { get; private set; }

    public Material VoxelMaterial;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        chunks = new Dictionary<Vector3, Chunk>();
        GenerateWorld();
    }

    private void GenerateWorld()
    {   // Generates a grid of chunks in the world using optimized single loop
        int totalChunks = worldSize.x * worldSize.x * worldSize.y;

        for (int i = 0; i < totalChunks; i++)
        {
            // Convert 1D index to 3D coordinates
            int x = i % worldSize.x;
            int y = (i / (worldSize.x * worldSize.x)) % worldSize.y; // Y coordinate is based on the second dimension of worldSize
            int z = (i / worldSize.x) % worldSize.x;

            Vector3 chunkPosition = new Vector3(x * chunkSize, y * chunkSize, z * chunkSize);
            if (!chunks.ContainsKey(chunkPosition))
            {
                GameObject chunkObject = new GameObject($"Chunk_{x}_{y}_{z}");
                chunkObject.transform.position = chunkPosition;
                Chunk chunk = chunkObject.AddComponent<Chunk>();
                chunk.Initialize(chunkSize);
                chunks[chunkPosition] = chunk;
            }
        }
    }

    public Voxel.VoxelType DetermineVoxelType(float x, float y, float z)
    {
        float noiseValue = SimplexNoise.Generate(x * 0.1f, y * 0.1f, z * 0.1f);
        float AirThreshold = 0f;

        //Debug.Log(noiseValue);

        if (noiseValue > AirThreshold)
            return Voxel.VoxelType.Grass; // Solid voxel
        else
            return Voxel.VoxelType.Air; // Air voxel
    }

    public Chunk GetChunk(Vector3 globalPosition)
    {
        Vector3 chunkPosition = new Vector3(
            Mathf.FloorToInt(globalPosition.x / chunkSize) * chunkSize,
            Mathf.FloorToInt(globalPosition.y / chunkSize) * chunkSize,
            Mathf.FloorToInt(globalPosition.z / chunkSize) * chunkSize
        );

        if (chunks.TryGetValue(chunkPosition, out Chunk chunk))
            return chunk;

        // Chunk not found
        return null;
    }


}