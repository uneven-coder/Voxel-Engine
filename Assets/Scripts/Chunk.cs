using System.Collections.Generic;
using UnityEngine;

public class Chunk : MonoBehaviour
{
    private Voxel[,,] voxels;
    [SerializeField] private int chunkSize = 16;

    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private List<Vector2> uvs = new List<Vector2>();

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider; void Start()
    {
        // Initialize Mesh Components
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = gameObject.AddComponent<MeshCollider>();

        // Initialize voxels if not already done
        if (voxels == null)
        {
            voxels = new Voxel[chunkSize, chunkSize, chunkSize];
            InitializeVoxels();
        }

        // Call this to generate the chunk mesh
        GenerateMesh();
    }

    public void Initialize(int size)
    {   // a external meathoud so the chunk can be generated at runtime
        // this is useful for procedural generation or when loading chunks from a file
        this.chunkSize = size;
        voxels = new Voxel[size, size, size];
        InitializeVoxels();
    }

    private void GenerateMesh()
    {
        // Clear previous mesh data
        vertices.Clear();
        triangles.Clear();
        uvs.Clear();

        // Process all voxels to generate visible faces
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    processVoxel(x, y, z);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();

        mesh.RecalculateNormals(); // for lighting

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
        // meshRenderer.material = ...;
        meshRenderer.material = World.Instance.VoxelMaterial;
        meshRenderer.material.color = voxels[0, 0, 0].color;
    }

    public void IterateVoxels(System.Action<Voxel> action = null) // remove null action later
    {   // iterates through all the voxels in the chunk and applies the action to each voxel
        int totalVoxels = chunkSize * chunkSize * chunkSize;

        for (int i = 0; i < totalVoxels; i++)
        {
            // Convert 1D index to 3D coordinates
            int x = i % chunkSize;
            int y = (i / chunkSize) % chunkSize;
            int z = i / (chunkSize * chunkSize);

            action(voxels[x, y, z]);
        }
    }


    private void InitializeVoxels()
    {   // gets all the voxels in the chunk and initializes them using optimized single loop
        int totalVoxels = chunkSize * chunkSize * chunkSize;

        for (int i = 0; i < totalVoxels; i++)
        {
            // Convert 1D index to 3D coordinates
            int x = i % chunkSize;
            int y = (i / chunkSize) % chunkSize;
            int z = i / (chunkSize * chunkSize);

            // Calculate world position for terrain generation
            Vector3 worldPos = transform.position + new Vector3(x, y, z);
            
            // Determine voxel type based on terrain generation
            Voxel.VoxelType voxelType = World.Instance.DetermineVoxelType(worldPos.x, worldPos.y, worldPos.z);
            
            // Set color based on voxel type
            Color voxelColor = Color.white;
            bool isActive = true;
            
            switch (voxelType)
            {
                case Voxel.VoxelType.Air:
                    voxelColor = Color.clear;
                    isActive = false;
                    break;
                case Voxel.VoxelType.Grass:
                    voxelColor = Color.green;
                    break;
                case Voxel.VoxelType.Stone:
                    voxelColor = Color.gray;
                    break;
            }
            
            voxels[x, y, z] = new Voxel(new Vector3(x, y, z), voxelColor, voxelType, isActive);
        }
    }

    private void processVoxel(int x, int y, int z)
    {   // Check a voxel and process it removing faces and adding meshes

        // check voxels exist and in bounds
        if (voxels == null || x < 0 || x >= voxels.GetLength(0) || y < 0 || y >= voxels.GetLength(1) || z < 0 || z >= voxels.GetLength(2))
            return; // Exit if voxel is out of bounds

        Voxel voxel = voxels[x, y, z];

        if (voxel.isActive)
        {
            // Check each face direction and add face data if visible
            Vector3Int[] directions = new Vector3Int[]
            {
                new Vector3Int(-1, 0, 0), // Left (-X)
                new Vector3Int(1, 0, 0),  // Right (+X)
                new Vector3Int(0, -1, 0), // Bottom (-Y)
                new Vector3Int(0, 1, 0),  // Top (+Y)
                new Vector3Int(0, 0, -1), // Back (-Z)
                new Vector3Int(0, 0, 1)   // Front (+Z)
            };

            for (int i = 0; i < 6; i++)
            {
                Vector3Int neighborPos = new Vector3Int(x, y, z) + directions[i];
                if (IsFaceVisible(neighborPos.x, neighborPos.y, neighborPos.z))
                {
                    AddFaceData(x, y, z, i);
                }
            }
        }
    }

    private bool IsFaceVisible(int x, int y, int z)
    {
        bool IsVoxelHiddenInChunk(int x, int y, int z)
        {
            if (x < 0 || x >= chunkSize || y < 0 || y >= chunkSize || z < 0 || z >= chunkSize)
                return true; // Face is at the boundary of the chunk
            return !voxels[x, y, z].isActive;
        }

        bool IsVoxelHiddenInWorld(Vector3 globalPos)
        {
            // Check if there is a chunk at the global position
            Chunk neighborChunk = World.Instance.GetChunk(globalPos);
            if (neighborChunk == null)
            {
                // No chunk at this position, so the voxel face should be hidden
                return true;
            }

            // Convert the global position to the local position within the neighboring chunk
            Vector3 localPos = neighborChunk.transform.InverseTransformPoint(globalPos);

            // If the voxel at this local position is inactive, the face should be visible (not hidden)
            return !neighborChunk.IsVoxelActiveAt(localPos);
        }

        // Convert local chunk coordinates to global coordinates
        Vector3 globalPos = transform.position + new Vector3(x, y, z);

        // Check if the neighboring voxel is inactive or out of bounds in the current chunk
        // and also if it's inactive or out of bounds in the world (neighboring chunks)
        return IsVoxelHiddenInChunk(x, y, z) && IsVoxelHiddenInWorld(globalPos);
    }

    public bool IsVoxelActiveAt(Vector3 localPosition)
    {
        // Round the local position to get the nearest voxel index
        int x = Mathf.RoundToInt(localPosition.x);
        int y = Mathf.RoundToInt(localPosition.y);
        int z = Mathf.RoundToInt(localPosition.z);

        // Check if the indices are within the bounds of the voxel array
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
        {
            // Return the active state of the voxel at these indices
            return voxels[x, y, z].isActive;
        }

        // If out of bounds, consider the voxel inactive
        return false;
    }

    private void AddFaceData(int x, int y, int z, int faceIndex)
    {   // Add vertex and triangle data for the specified face of the voxel

        // Vertex positions for each face type - matching direction array order
        Vector3[][] faceVertices = new Vector3[][]
        {
                // 0: Left Face (-X)
                new Vector3[]
                {
                    new Vector3(x, y,     z    ),
                    new Vector3(x, y + 1, z    ),
                    new Vector3(x, y + 1, z + 1),
                    new Vector3(x, y,     z + 1)
                },
                // 1: Right Face (+X)
                new Vector3[]
                {
                    new Vector3(x + 1, y,     z + 1),
                    new Vector3(x + 1, y + 1, z + 1),
                    new Vector3(x + 1, y + 1, z    ),
                    new Vector3(x + 1, y,     z    )
                },
                // 2: Bottom Face (-Y)
                new Vector3[]
                {
                    new Vector3(x,     y, z    ),
                    new Vector3(x,     y, z + 1),
                    new Vector3(x + 1, y, z + 1),
                    new Vector3(x + 1, y, z    )
                },
                // 3: Top Face (+Y)
                new Vector3[]
                {
                    new Vector3(x,     y + 1, z    ),
                    new Vector3(x + 1, y + 1, z    ),
                    new Vector3(x + 1, y + 1, z + 1),
                    new Vector3(x,     y + 1, z + 1)
                },
                // 4: Back Face (-Z)
                new Vector3[]
                {
                    new Vector3(x + 1, y,     z    ),
                    new Vector3(x + 1, y + 1, z    ),
                    new Vector3(x,     y + 1, z    ),
                    new Vector3(x,     y,     z    )
                },
                // 5: Front Face (+Z)
                new Vector3[]
                {
                    new Vector3(x,     y,     z + 1),
                    new Vector3(x,     y + 1, z + 1),
                    new Vector3(x + 1, y + 1, z + 1),
                    new Vector3(x + 1, y,     z + 1)
                }
        };        // UV coordinates for each face
        Vector2[][] faceUVs = new Vector2[][]
        {
                // 0: Left Face (-X)
                new Vector2[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                // 1: Right Face (+X)
                new Vector2[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                // 2: Bottom Face (-Y)
                new Vector2[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                // 3: Top Face (+Y)
                new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                // 4: Back Face (-Z)
                new Vector2[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                // 5: Front Face (+Z)
                new Vector2[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) }
        };

        // Add vertices and UVs for the selected face
        for (int i = 0; i < 4; i++)
        {
            vertices.Add(faceVertices[faceIndex][i]);
            uvs.Add(faceUVs[faceIndex][i]);
        }

        AddTriangleIndices();
    }

    private void AddTriangleIndices()
    {
        // Get the starting index for the last 4 vertices we added
        int vertCount = vertices.Count;

        // First triangle (counter-clockwise winding)
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 2);
        triangles.Add(vertCount - 3);

        // Second triangle (counter-clockwise winding)
        triangles.Add(vertCount - 4);
        triangles.Add(vertCount - 1);
        triangles.Add(vertCount - 2);
    }
}