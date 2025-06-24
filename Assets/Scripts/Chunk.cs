using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Linq;

public class Chunk : MonoBehaviour
{
    private Voxel[,,] voxels;
    [SerializeField] private int chunkSize = 16;

    // Use pooled lists to reduce allocations
    private static readonly Stack<List<Vector3>> verticesPool = new Stack<List<Vector3>>();
    private static readonly Stack<List<int>> trianglesPool = new Stack<List<int>>();
    private static readonly Stack<List<Vector2>> uvsPool = new Stack<List<Vector2>>();

    private List<Vector3> vertices;
    private List<int> triangles;
    private List<Vector2> uvs;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    private static readonly Vector3Int[] directions =
    {
        new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
        new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
        new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1)
    };

    private static readonly Vector3[][] faceVertices =
    {
        new Vector3[] { new Vector3(0,0,0), new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0) }, // -X
        new Vector3[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // +X
        new Vector3[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) }, // -Y
        new Vector3[] { new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) }, // +Y
        new Vector3[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }, // -Z
        new Vector3[] { new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1) }  // +Z
    };

    private static readonly Vector2[] faceUVs =
    {
        new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1)
    };

    void Start()
    {   // Initialize mesh components and voxel data on start
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = gameObject.AddComponent<MeshCollider>();

        if (voxels == null)
        {   // Create and initialize voxels if they don't exist
            voxels = new Voxel[chunkSize, chunkSize, chunkSize];
            InitializeVoxels();
        }

        vertices = verticesPool.Count > 0 ? verticesPool.Pop() : new List<Vector3>(4096);
        triangles = trianglesPool.Count > 0 ? trianglesPool.Pop() : new List<int>(4096);
        uvs = uvsPool.Count > 0 ? uvsPool.Pop() : new List<Vector2>(4096);

        // Mark chunk as static for Unity's static batching to reduce draw calls
        gameObject.isStatic = true;

        GenerateMesh();
    }

    public void Initialize(int size)
    {   // Set chunk size and initialize voxel data
        this.chunkSize = size;
        voxels = new Voxel[size, size, size];

        InitializeVoxels();
    }

    // Make mesh regeneration public for world updates
    public void GenerateMesh()
    {
        // Ensure required components are present
        if (!TryEnsureMeshComponents() || voxels == null)
            return;

        vertices.Clear();
        triangles.Clear();
        uvs.Clear();

        // Flatten and filter active voxels with their 3D coordinates using LINQ
        voxels.Cast<Voxel>()
              .Select((voxel, index) => new
              {
                  voxel,
                  x = index % chunkSize,
                  y = (index / (chunkSize * chunkSize)) % chunkSize,
                  z = (index / chunkSize) % chunkSize
              })
              .Where(v => v.voxel.isActive)
              .ToList()
              .ForEach(v => AddVisibleFaces(v.x, v.y, v.z));

        // Create and assign the mesh
        Mesh mesh = new Mesh {
            indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, calculateBounds: true);
        mesh.RecalculateNormals();
        mesh.UploadMeshData(markNoLongerReadable: true);

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;

        // Set the material and initial color
        meshRenderer.material = World.Instance.VoxelMaterial;
        meshRenderer.material.color = voxels[0, 0, 0].color;
    }

    void OnDestroy()
    {   // Return pooled lists to prevent memory leaks
        if (vertices != null)
            vertices.Clear(); verticesPool.Push(vertices);

        if (triangles != null)
            triangles.Clear(); trianglesPool.Push(triangles);

        if (uvs != null)
            uvs.Clear(); uvsPool.Push(uvs);
    }

    private bool TryEnsureMeshComponents()
    {   // Ensure all mesh-related components are attached to the GameObject
    
        if (meshFilter == null || meshRenderer == null || meshCollider == null)
        {   // If any component is missing, try to get or add them
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
        }

        return meshFilter != null && meshRenderer != null && meshCollider != null;
    }

    private void AddVisibleFaces(int x, int y, int z)
    {   // Check all 6 faces of a voxel and add the visible ones to the mesh
        for (int i = 0; i < 6; i++)
        {   // Check each face direction
            Vector3Int dir = directions[i];
            int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;

            if (IsNeighborFaceVisible(nx, ny, nz, dir, x, y, z))
                AddFaceData(x, y, z, i);
        }
    }

    private bool IsNeighborFaceVisible(int nx, int ny, int nz, Vector3Int dir, int x, int y, int z)
    {   // Check if a voxel face is visible, accounting for chunk boundaries
        if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkSize || nz < 0 || nz >= chunkSize)
        {   // Handle faces at the edge of the chunk
            Vector3 neighborWorldPos = transform.position + new Vector3(x, y, z) + new Vector3(dir.x, dir.y, dir.z);
            Chunk neighborChunk = World.Instance.GetChunk(neighborWorldPos);

            if (neighborChunk == null)
                return true; // No neighbor chunk, show face

            Vector3 localPos = neighborChunk.transform.InverseTransformPoint(neighborWorldPos);
            return !neighborChunk.IsVoxelActiveAt(localPos);
        }

        return !voxels[nx, ny, nz].isActive;
    }

    public bool IsVoxelActiveAt(Vector3 localPosition)
    {   // Check if a voxel at a local position is active
        int x = Mathf.RoundToInt(localPosition.x);
        int y = Mathf.RoundToInt(localPosition.y);
        int z = Mathf.RoundToInt(localPosition.z);

        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
            return voxels[x, y, z].isActive;

        return false;
    }

    private void AddFaceData(int x, int y, int z, int faceIndex)
    {   // Add vertices, triangles, and UVs for a single voxel face
        int vertStart = vertices.Count;
        Vector3 basePos = new Vector3(x, y, z);
        var faceVerts = faceVertices[faceIndex];

        for (int i = 0; i < 4; i++)
        {   // Add 4 vertices for the face
            vertices.Add(basePos + faceVerts[i]);
            uvs.Add(faceUVs[i]);
        }

        // Use array for triangle indices for better cache
        int[] tris = { 0, 1, 2, 2, 3, 0 };
        for (int t = 0; t < 6; t++)
            triangles.Add(vertStart + tris[t]);
    }

    private void InitializeVoxels()
    {   // Populate the voxels array with initial data based on world generation
        int totalVoxels = chunkSize * chunkSize * chunkSize;
        Vector3 chunkPos = transform.position; // Avoid repeated transform.position calls

        // Use a single loop for better cache locality
        for (int i = 0; i < totalVoxels; i++)
        {   // Calculate 3D indices from 1D index
            int x = i % chunkSize;
            int y = (i / (chunkSize * chunkSize)) % chunkSize;
            int z = (i / chunkSize) % chunkSize;

            // Determine voxel type and color
            float wx = chunkPos.x + x;
            float wy = chunkPos.y + y;
            float wz = chunkPos.z + z;
            Voxel.VoxelType type = World.Instance.DetermineVoxelType(wx, wy, wz);
            Color color = type == Voxel.VoxelType.Grass ? Color.green : (type == Voxel.VoxelType.Stone ? Color.gray : Color.clear);

            // Create the new voxel
            voxels[x, y, z] = new Voxel(new Vector3(x, y, z), color, type, type != Voxel.VoxelType.Air);
        }
    }

    public void IterateVoxels(System.Action<Voxel> action = null)
    {   // Execute an action for each voxel in the chunk
        if (action == null)
            return;

        int totalVoxels = chunkSize * chunkSize * chunkSize;
        for (int i = 0; i < totalVoxels; i++)
        {   // Iterate through all voxels
            int x = i % chunkSize;
            int y = (i / (chunkSize * chunkSize)) % chunkSize;
            int z = (i / chunkSize) % chunkSize;
            action(voxels[x, y, z]);
        }
    }

    // Job struct for mesh data generation
    private struct MeshGenJob : IJob
    {
        [ReadOnly] public NativeArray<Voxel> voxels;
        public int chunkSize;
        public NativeArray<Vector3> vertices;
        public NativeArray<int> triangles;
        public NativeArray<Vector2> uvs;
        public NativeArray<int> vertexCount;
        public NativeArray<int> triangleCount;
        public NativeArray<int> uvCount;

        public void Execute()
        {   // Generate mesh data in a job
            int vertIndex = 0;
            int triIndex = 0;
            int uvIndex = 0;
            int[] tris = { 0, 1, 2, 2, 3, 0 };
            Vector3[] faceVerts = new Vector3[4];
            Vector2[] faceUVs = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            Vector3Int[] directions = {
                new Vector3Int(-1,0,0), new Vector3Int(1,0,0),
                new Vector3Int(0,-1,0), new Vector3Int(0,1,0),
                new Vector3Int(0,0,-1), new Vector3Int(0,0,1)
            };
            Vector3[][] faceVertices = {
                new Vector3[] { new Vector3(0,0,0), new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0) }, // -X
                new Vector3[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // +X
                new Vector3[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) }, // -Y
                new Vector3[] { new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) }, // +Y
                new Vector3[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }, // -Z
                new Vector3[] { new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1) }  // +Z
            };
            int totalVoxels = chunkSize * chunkSize * chunkSize;

            for (int i = 0; i < totalVoxels; i++)
            {   // Iterate through each voxel
                Voxel voxel = voxels[i];

                if (!voxel.isActive)
                    continue;

                int x = i % chunkSize;
                int y = (i / (chunkSize * chunkSize)) % chunkSize;
                int z = (i / chunkSize) % chunkSize;

                for (int f = 0; f < 6; f++)
                {   // Check each of the 6 faces
                    Vector3Int dir = directions[f];
                    int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;
                    bool neighborActive = false;

                    if (nx >= 0 && nx < chunkSize && ny >= 0 && ny < chunkSize && nz >= 0 && nz < chunkSize)
                    {   // Check if neighbor is inside the same chunk
                        int ni = nx + ny * chunkSize + nz * chunkSize * chunkSize;
                        neighborActive = voxels[ni].isActive;
                    }

                    // Only add face if neighbor is inactive or out of bounds
                    if (!neighborActive)
                    {   // Add face data if neighbor is inactive or out of bounds
                        Vector3 basePos = new Vector3(x, y, z);

                        for (int v = 0; v < 4; v++)
                        {   // Add 4 vertices for the face
                            vertices[vertIndex] = basePos + faceVertices[f][v];
                            uvs[uvIndex] = faceUVs[v];
                            vertIndex++;
                            uvIndex++;
                        }

                        for (int t = 0; t < 6; t++)
                        {   // Add 6 triangle indices for the 2 triangles of the face
                            triangles[triIndex] = vertIndex - 4 + tris[t];
                            triIndex++;
                        }
                    }
                }
            }

            vertexCount[0] = vertIndex;
            triangleCount[0] = triIndex;
            uvCount[0] = uvIndex;
        }
    }
}