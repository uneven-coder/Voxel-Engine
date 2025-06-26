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
    [SerializeField] private ComputeShader voxelComputeShader;

    // Use pooled lists to reduce allocations
    private static readonly Stack<List<Vector3>> verticesPool = new Stack<List<Vector3>>();
    private static readonly Stack<List<int>> trianglesPool = new Stack<List<int>>();
    private static readonly Stack<List<Vector2>> uvsPool = new Stack<List<Vector2>>();

    private List<Vector3> vertices;
    private List<int> triangles;
    private List<Vector2> uvs;
    
    // Compute shader data
    private bool useComputeShader = true;

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

        // Initialize compute shader if available
        if (voxelComputeShader != null && useComputeShader)
        {   // Setup compute shader for this chunk - placeholder for future implementation
            Debug.Log("Compute shader voxel processing will be implemented");
        }

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

    public void InitializeWithVoxelData(int size, Voxel[,,] preGeneratedVoxels)
    {   // Set chunk size and use pre-generated voxel data
        this.chunkSize = size;
        voxels = new Voxel[size, size, size];

        // Copy pre-generated voxel data
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                    voxels[x, y, z] = preGeneratedVoxels[x, y, z];
            }
        }
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

        // Group voxels by type and generate mesh data for each type
        var activeVoxels = new List<(Voxel voxel, int x, int y, int z)>();
        
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    var voxel = voxels[x, y, z];
                    if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
                        activeVoxels.Add((voxel, x, y, z));
                }
            }
        }
        
        var voxelsByType = activeVoxels
            .GroupBy(v => v.voxel.type)
            .ToList();

        // Create submeshes for each voxel type
        var submeshTriangles = new List<List<int>>();
        foreach (var typeGroup in voxelsByType)
        {   // Process each voxel type separately
            var typeTriangles = new List<int>();
            foreach (var voxelData in typeGroup)
                AddVisibleFaces(voxelData.x, voxelData.y, voxelData.z, typeTriangles);
            submeshTriangles.Add(typeTriangles);
        }

        // Destroy existing mesh to prevent memory leaks
        if (meshFilter.sharedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(meshFilter.sharedMesh);
            else
                DestroyImmediate(meshFilter.sharedMesh);
        }

        // Create and configure the mesh
        Mesh mesh = new Mesh
        {
            name = $"ChunkMesh_{transform.position.x}_{transform.position.y}_{transform.position.z}",
            indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16,
            subMeshCount = Mathf.Max(1, submeshTriangles.Count)
        };

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);

        // Set submesh triangles for each voxel type
        if (submeshTriangles.Count > 0)
        {
            for (int i = 0; i < submeshTriangles.Count; i++)
                mesh.SetTriangles(submeshTriangles[i], i, calculateBounds: false);
        }
        else
        {   // Empty mesh case - still need at least one submesh
            mesh.SetTriangles(new int[0], 0, calculateBounds: false);
        }

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.UploadMeshData(markNoLongerReadable: true);

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;

        // Create materials array for each voxel type
        if (voxelsByType.Count > 0)
            SetupMaterialsForVoxelTypes(voxelsByType.Select(g => g.Key).ToArray());
        else
        {   // No active voxels - set empty material array
            meshRenderer.materials = new Material[0];
        }
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

    private void AddVisibleFaces(int x, int y, int z, List<int> typeTriangles)
    {   // Check all 6 faces of a voxel and add the visible ones to the specific type's triangle list
        for (int i = 0; i < 6; i++)
        {   // Check each face direction
            Vector3Int dir = directions[i];
            int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;

            if (IsNeighborFaceVisible(nx, ny, nz, dir, x, y, z))
                AddFaceData(x, y, z, i, typeTriangles);
        }
    }

    private bool IsNeighborFaceVisible(int nx, int ny, int nz, Vector3Int dir, int x, int y, int z)
    {   // Check if a voxel face is visible, accounting for chunk boundaries
        if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkSize || nz < 0 || nz >= chunkSize)
        {   // Handle faces at the edge of the chunk - check neighboring chunk
            Vector3 neighborWorldPos = transform.position + new Vector3(nx, ny, nz);
            Chunk neighborChunk = World.Instance.GetChunk(neighborWorldPos);

            if (neighborChunk == null)
                return true; // No neighbor chunk, show face

            // Convert to neighbor chunk's local coordinates
            Vector3 localPos = neighborChunk.transform.InverseTransformPoint(neighborWorldPos);
            int localX = Mathf.FloorToInt(localPos.x);
            int localY = Mathf.FloorToInt(localPos.y);
            int localZ = Mathf.FloorToInt(localPos.z);

            // Handle wrapping coordinates at chunk boundaries
            if (localX < 0) localX = chunkSize - 1;
            else if (localX >= chunkSize) localX = 0;
            
            if (localY < 0) localY = chunkSize - 1;
            else if (localY >= chunkSize) localY = 0;
            
            if (localZ < 0) localZ = chunkSize - 1;
            else if (localZ >= chunkSize) localZ = 0;

            // Check if neighbor voxel is solid
            return !neighborChunk.IsVoxelActiveAt(localX, localY, localZ);
        }

        // Neighbor is within this chunk - check if it's transparent
        return !IsVoxelSolid(nx, ny, nz);
    }

    private bool IsVoxelSolid(int x, int y, int z)
    {   // Check if voxel at coordinates is solid (active and not air)
        if (x < 0 || x >= chunkSize || y < 0 || y >= chunkSize || z < 0 || z >= chunkSize)
            return false;
            
        return voxels[x, y, z].isActive && voxels[x, y, z].type != Voxel.VoxelType.Air;
    }

    public bool IsVoxelActiveAt(Vector3 localPosition)
    {   // Check if a voxel at a local position is active and not air
        int x = Mathf.FloorToInt(localPosition.x);
        int y = Mathf.FloorToInt(localPosition.y);
        int z = Mathf.FloorToInt(localPosition.z);

        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
            return voxels[x, y, z].isActive && voxels[x, y, z].type != Voxel.VoxelType.Air;

        return false;
    }

    public bool IsVoxelActiveAt(int x, int y, int z)
    {   // Check if a voxel at specific coordinates is active and not air
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
            return voxels[x, y, z].isActive && voxels[x, y, z].type != Voxel.VoxelType.Air;

        return false;
    }

    private void AddFaceData(int x, int y, int z, int faceIndex, List<int> typeTriangles)
    {   // Add vertices, triangles, and UVs for a single voxel face to specific type list
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
            typeTriangles.Add(vertStart + tris[t]);
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

            // Determine voxel type and create voxel using VoxelTypeManager
            float wx = chunkPos.x + x;
            float wy = chunkPos.y + y;
            float wz = chunkPos.z + z;
            Voxel.VoxelType type = World.Instance.DetermineVoxelType(wx, wy, wz);

            // Create the new voxel using VoxelTypeManager
            voxels[x, y, z] = VoxelTypeManager.CreateVoxel(new Vector3(x, y, z), type);
        }
    }

    public void IterateVoxels(System.Action<int, int, int> action = null)
    {   // Execute an action for each voxel coordinate in the chunk
        if (action == null)
            return;

        int totalVoxels = chunkSize * chunkSize * chunkSize;
        for (int i = 0; i < totalVoxels; i++)
        {   // Iterate through all voxels and pass coordinates
            int x = i % chunkSize;
            int y = (i / (chunkSize * chunkSize)) % chunkSize;
            int z = (i / chunkSize) % chunkSize;
            action(x, y, z);
        }
    }

    public Voxel GetVoxelAt(int x, int y, int z)
    {   // Get voxel at specific coordinates
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
            return voxels[x, y, z];
        
        return new Voxel(Vector3.zero, Color.clear, Voxel.VoxelType.Air, false);
    }

    public void SetVoxelType(int x, int y, int z, Voxel.VoxelType newType)
    {   // Update voxel type at specific coordinates using VoxelTypeManager
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize)
            voxels[x, y, z] = VoxelTypeManager.CreateVoxel(new Vector3(x, y, z), newType);
    }

    private Color GetDominantVoxelColor()
    {   // Find the most common active voxel type and return its color
        var typeCounts = new Dictionary<Voxel.VoxelType, int>();
        int totalVoxels = chunkSize * chunkSize * chunkSize;

        // Count occurrences of each active voxel type
        for (int i = 0; i < totalVoxels; i++)
        {   // Calculate coordinates and count active voxel types
            int x = i % chunkSize;
            int y = (i / (chunkSize * chunkSize)) % chunkSize;
            int z = (i / chunkSize) % chunkSize;

            if (voxels[x, y, z].isActive)
            {
                var type = voxels[x, y, z].type;
                typeCounts[type] = typeCounts.ContainsKey(type) ? typeCounts[type] + 1 : 1;
            }
        }

        // Find the most common type or default to Grass
        var dominantType = typeCounts.Count > 0 
            ? typeCounts.Aggregate((x, y) => x.Value > y.Value ? x : y).Key
            : Voxel.VoxelType.Grass;

        return VoxelTypeManager.GetVoxelColor(dominantType);
    }

    private void SetupMaterialsForVoxelTypes(Voxel.VoxelType[] voxelTypes)
    {   // Create and assign materials for each voxel type submesh
        if (World.Instance?.VoxelMaterial == null || voxelTypes.Length == 0)
        {
            meshRenderer.materials = new Material[0];
            return;
        }

        Material[] materials = new Material[voxelTypes.Length];
        
        for (int i = 0; i < voxelTypes.Length; i++)
        {   // Create material instance for each voxel type
            materials[i] = new Material(World.Instance.VoxelMaterial);
            materials[i].color = VoxelTypeManager.GetVoxelColor(voxelTypes[i]);
        }
        
        meshRenderer.materials = materials;
    }

    public void DestroyVoxelsWithComputeShader(Vector3 destructionCenter, int destructionRadius, int[] voxelIndices)
    {   // Use compute shader for fast batch voxel destruction when available
        if (voxelIndices == null || voxelIndices.Length == 0)
            return;

        // For now, fall back to traditional destruction until compute shader is fully implemented
        foreach (int index in voxelIndices)
        {   // Convert 1D index to 3D coordinates
            int x = index % chunkSize;
            int y = (index / (chunkSize * chunkSize)) % chunkSize;
            int z = (index / chunkSize) % chunkSize;
            
            if (IsWithinBounds(x, y, z))
            {   // Destroy voxel at calculated coordinates
                var voxel = voxels[x, y, z];
                if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
                {   // Convert to air type
                    SetVoxelType(x, y, z, Voxel.VoxelType.Air);
                    World.Instance.StoreVoxelChange(this, x, y, z, Voxel.VoxelType.Air);
                }
            }
        }
        
        // Regenerate mesh after batch destruction
        GenerateMesh();
    }

    private bool IsWithinBounds(int x, int y, int z) =>
        x >= 0 && x < chunkSize && y >= 0 && y < chunkSize && z >= 0 && z < chunkSize;
}