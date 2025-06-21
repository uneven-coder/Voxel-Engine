using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

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
    {
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = gameObject.AddComponent<MeshCollider>();
        if (voxels == null)
        {
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
    {
        this.chunkSize = size;
        voxels = new Voxel[size, size, size];
        InitializeVoxels();
    }

    // Make mesh regeneration public for world updates
    public void GenerateMesh()
    {
        if (!TryEnsureMeshComponents()) return;
        if (voxels == null) return;
        vertices.Clear();
        triangles.Clear();
        uvs.Clear();
        for (int x = 0; x < chunkSize; x++)
            for (int y = 0; y < chunkSize; y++)
                for (int z = 0; z < chunkSize; z++)
                    if (voxels[x, y, z].isActive)
                        AddVisibleFaces(x, y, z);
        Mesh mesh = new Mesh();
        mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        // Upload mesh data to GPU and release CPU copy for memory efficiency
        mesh.UploadMeshData(true);
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
        meshRenderer.material = World.Instance.VoxelMaterial;
        meshRenderer.material.color = voxels[0, 0, 0].color;
    }

    void OnDestroy()
    {
        // Return lists to pool for reuse
        if (vertices != null) { vertices.Clear(); verticesPool.Push(vertices); }
        if (triangles != null) { triangles.Clear(); trianglesPool.Push(triangles); }
        if (uvs != null) { uvs.Clear(); uvsPool.Push(uvs); }
    }

    // Ensures all mesh components are present, returns false if any cannot be created
    private bool TryEnsureMeshComponents()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>() ?? gameObject.AddComponent<MeshCollider>();
        return meshFilter != null && meshRenderer != null && meshCollider != null;
    }

    private void AddVisibleFaces(int x, int y, int z)
    {
        // Unroll loop for 6 faces for performance
        for (int i = 0; i < 6; i++)
        {
            Vector3Int dir = directions[i];
            int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;
            if (IsNeighborFaceVisible(nx, ny, nz, dir, x, y, z))
                AddFaceData(x, y, z, i);
        }
    }

    // Checks if a face is visible, including across chunk boundaries
    private bool IsNeighborFaceVisible(int nx, int ny, int nz, Vector3Int dir, int x, int y, int z)
    {
        if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkSize || nz < 0 || nz >= chunkSize)
        {
            // Check neighbor chunk
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
    {
        int x = Mathf.RoundToInt(localPosition.x);
        int y = Mathf.RoundToInt(localPosition.y);
        int z = Mathf.RoundToInt(localPosition.z);
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
            return voxels[x, y, z].isActive;
        return false;
    }

    private void AddFaceData(int x, int y, int z, int faceIndex)
    {
        int vertStart = vertices.Count;
        Vector3 basePos = new Vector3(x, y, z);
        var faceVerts = faceVertices[faceIndex];
        for (int i = 0; i < 4; i++)
        {
            vertices.Add(basePos + faceVerts[i]);
            uvs.Add(faceUVs[i]);
        }
        // Use array for triangle indices for better cache
        int[] tris = {0, 1, 2, 2, 3, 0};
        for (int t = 0; t < 6; t++)
            triangles.Add(vertStart + tris[t]);
    }

    private void InitializeVoxels()
    {
        int totalVoxels = chunkSize * chunkSize * chunkSize;
        // Use a single loop for better cache locality
        for (int i = 0; i < totalVoxels; i++)
        {
            int x = i % chunkSize;
            int y = (i / (chunkSize * chunkSize)) % chunkSize;
            int z = (i / chunkSize) % chunkSize;
            // Avoid repeated transform.position calls
            Vector3 chunkPos = transform.position;
            float wx = chunkPos.x + x;
            float wy = chunkPos.y + y;
            float wz = chunkPos.z + z;
            Voxel.VoxelType type = World.Instance.DetermineVoxelType(wx, wy, wz);
            Color color = type == Voxel.VoxelType.Grass ? Color.green : (type == Voxel.VoxelType.Stone ? Color.gray : Color.clear);
            voxels[x, y, z] = new Voxel(new Vector3(x, y, z), color, type, type != Voxel.VoxelType.Air);
        }
    }

    public void IterateVoxels(System.Action<Voxel> action = null)
    {
        if (action == null) return;
        int totalVoxels = chunkSize * chunkSize * chunkSize;
        for (int i = 0; i < totalVoxels; i++)
        {
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
        {
            int vertIndex = 0;
            int triIndex = 0;
            int uvIndex = 0;
            int[] tris = {0, 1, 2, 2, 3, 0};
            Vector3[] faceVerts = new Vector3[4];
            Vector2[] faceUVs = { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
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
            {
                Voxel voxel = voxels[i];
                if (!voxel.isActive) continue;
                int x = i % chunkSize;
                int y = (i / (chunkSize * chunkSize)) % chunkSize;
                int z = (i / chunkSize) % chunkSize;
                for (int f = 0; f < 6; f++)
                {
                    Vector3Int dir = directions[f];
                    int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;
                    bool neighborActive = false;
                    if (nx >= 0 && nx < chunkSize && ny >= 0 && ny < chunkSize && nz >= 0 && nz < chunkSize)
                    {
                        int ni = nx + ny * chunkSize + nz * chunkSize * chunkSize;
                        neighborActive = voxels[ni].isActive;
                    }
                    // Only add face if neighbor is inactive or out of bounds
                    if (!neighborActive)
                    {
                        Vector3 basePos = new Vector3(x, y, z);
                        for (int v = 0; v < 4; v++)
                        {
                            vertices[vertIndex] = basePos + faceVertices[f][v];
                            uvs[uvIndex] = faceUVs[v];
                            vertIndex++;
                            uvIndex++;
                        }
                        for (int t = 0; t < 6; t++)
                        {
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
    // Example method to schedule mesh generation job (not yet used in GenerateMesh)
    private void GenerateMeshJobified()
    {
        int maxVerts = chunkSize * chunkSize * chunkSize * 24; // Max possible verts (6 faces * 4 verts)
        int maxTris = chunkSize * chunkSize * chunkSize * 36; // Max possible tris (6 faces * 6 indices)
        var nativeVoxels = new NativeArray<Voxel>(chunkSize * chunkSize * chunkSize, Allocator.TempJob);
        for (int x = 0; x < chunkSize; x++)
            for (int y = 0; y < chunkSize; y++)
                for (int z = 0; z < chunkSize; z++)
                    nativeVoxels[x + y * chunkSize + z * chunkSize * chunkSize] = voxels[x, y, z];
        var nativeVertices = new NativeArray<Vector3>(maxVerts, Allocator.TempJob);
        var nativeTriangles = new NativeArray<int>(maxTris, Allocator.TempJob);
        var nativeUVs = new NativeArray<Vector2>(maxVerts, Allocator.TempJob);
        var vertexCount = new NativeArray<int>(1, Allocator.TempJob);
        var triangleCount = new NativeArray<int>(1, Allocator.TempJob);
        var uvCount = new NativeArray<int>(1, Allocator.TempJob);
        var job = new MeshGenJob
        {
            voxels = nativeVoxels,
            chunkSize = chunkSize,
            vertices = nativeVertices,
            triangles = nativeTriangles,
            uvs = nativeUVs,
            vertexCount = vertexCount,
            triangleCount = triangleCount,
            uvCount = uvCount
        };
        JobHandle handle = job.Schedule();
        handle.Complete();
        // Copy data back to managed lists (vertices, triangles, uvs) up to counts
        // ...
        nativeVoxels.Dispose();
        nativeVertices.Dispose();
        nativeTriangles.Dispose();
        nativeUVs.Dispose();
        vertexCount.Dispose();
        triangleCount.Dispose();
        uvCount.Dispose();
    }
}