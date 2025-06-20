using System.Collections.Generic;
using UnityEngine;

public class Chunk : MonoBehaviour
{
    private Voxel[,,] voxels;
    [SerializeField] private int chunkSize = 16;

    void Start()
    {
        voxels = new Voxel[chunkSize, chunkSize, chunkSize];
        InitializeVoxels();
    }

    public void Initialize(int size)
    {   // a external meathoud so the chunk can be generated at runtime
        // this is useful for procedural generation or when loading chunks from a file
        this.chunkSize = size;
        voxels = new Voxel[size, size, size];
        InitializeVoxels();
        
    }

    private void InitializeVoxels()
    {   // gets all the voxels in the chunk and initializes them
        // this is a 3D array of voxels, so we need to loop through each dimension
        // optimsie not so many for loops
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    var randomColor = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), 1f);
                    var pastelColor = Color.Lerp(randomColor, Color.white, 0.5f);
                    voxels[x, y, z] = new Voxel(transform.position + new Vector3(x, y, z), pastelColor);
                }
            }
        }
    }


    void OnDrawGizmos()
    {
        if (voxels != null)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        // if (voxels[x, y, z].isActive)
                        // {
                            Gizmos.color = voxels[x, y, z].color;
                            Gizmos.DrawCube(transform.position + new Vector3(x, y, z), Vector3.one);
                        // }
                    }
                }
            }
        }
    }
}