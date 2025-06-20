using System;
using System.Collections.Generic;
using UnityEngine;

public class World : MonoBehaviour
{
    public int worldSize = 5; // Size of the world in chunks
    public int chunkSize = 16; // Size of each chunk

    private Dictionary<Vector3, Chunk> chunks;

    void Start()
    {
        chunks = new Dictionary<Vector3, Chunk>();
        GenerateWorld();
    }

    private void GenerateWorld()
    {
        for (int x = 0; x < worldSize; x++)
        {
            for (int y = 0; x < worldSize; y++)
            {
                for (int z = 0; x < worldSize; z++)
                {
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
        }
    }
}