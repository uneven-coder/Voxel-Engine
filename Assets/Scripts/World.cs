using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class World : MonoBehaviour
{
    public Vector2Int worldSize = new Vector2Int(5, 3); // World size in chunks (XZ, Y)
    public int chunkSize = 16; // Size of each chunk

    // Use Vector3Int for chunk keys for better hashing and performance
    private Dictionary<Vector3Int, Chunk> chunks;
    private Dictionary<Vector3Int, Voxel[,,]> chunkVoxelData; // Store voxel data for persistence
    private Dictionary<Vector3Int, Dictionary<Vector3Int, Voxel.VoxelType>> voxelChanges; // Store player modifications
    public static World Instance { get; private set; }
    public Material VoxelMaterial;

    // noise
    public int noiseSeed = 1234;
    public float maxHeight = 0.2f;
    public float noiseScale = 0.015f;
    public float[,] noiseArray;

    private Vector3Int worldOrigin; // Offset so center chunk is at World GameObject
    private CameraController cameraController;
    private Plane[] cameraFrustumPlanes;
    private Vector3Int lastCameraChunk = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
    private HashSet<Vector3Int> visibleChunks = new HashSet<Vector3Int>();
    private Dictionary<Vector3Int, int> chunkLastVisibleFrame = new Dictionary<Vector3Int, int>();
    private const int chunkUnloadDelayFrames = 10;
    private int lastVisibleChunksFrame = -1;
    private List<Vector3Int> chunksToUnload = new List<Vector3Int>();

    private void Awake()
    {   // Initialize singleton instance and calculate world positioning
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        noiseArray = GlobalNoise.GetNoise();

        {   // Calculate worldOrigin so center chunk is at World GameObject
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
    }

    private void Start()
    {   // Initialize world data structures and load initial chunks
        chunks = new Dictionary<Vector3Int, Chunk>(worldSize.x * worldSize.x * worldSize.y);
        chunkVoxelData = new Dictionary<Vector3Int, Voxel[,,]>(worldSize.x * worldSize.x * worldSize.y);
        voxelChanges = new Dictionary<Vector3Int, Dictionary<Vector3Int, Voxel.VoxelType>>();
        cameraController = FindObjectOfType<CameraController>();
        
        GenerateInitialChunkData();
        LoadAllChunks();
    }

    private void GenerateInitialChunkData()
    {   // Generate and store initial voxel data for all chunks
        for (int x = 0; x < worldSize.x; x++)
        {
            for (int y = 0; y < worldSize.y; y++)
            {
                for (int z = 0; z < worldSize.x; z++)
                {   // Create initial voxel data for each chunk coordinate
                    Vector3Int chunkCoord = new Vector3Int(x, y, z);
                    chunkVoxelData[chunkCoord] = GenerateChunkVoxelData(chunkCoord);
                }
            }
        }
    }

    private Voxel[,,] GenerateChunkVoxelData(Vector3Int chunkCoord)
    {   // Generate voxel data for a specific chunk with random modifications
        Voxel[,,] voxels = new Voxel[chunkSize, chunkSize, chunkSize];
        Vector3 chunkWorldPos = ChunkCoordToWorldPos(chunkCoord);
        int totalVoxels = chunkSize * chunkSize * chunkSize;

        // Generate base terrain data
        for (int i = 0; i < totalVoxels; i++)
        {   // Calculate 3D indices and create voxels
            int x = i % chunkSize;
            int y = (i / (chunkSize * chunkSize)) % chunkSize;
            int z = (i / chunkSize) % chunkSize;

            float wx = chunkWorldPos.x + x;
            float wy = chunkWorldPos.y + y;
            float wz = chunkWorldPos.z + z;
            Voxel.VoxelType baseType = DetermineVoxelType(wx, wy, wz);

            // Apply layered coloring to non-air voxels based on depth
            if (baseType != Voxel.VoxelType.Air)
                baseType = DetermineLayeredVoxelType(wy, wx, wz);

            voxels[x, y, z] = VoxelTypeManager.CreateVoxel(new Vector3(x, y, z), baseType);
        }

        // Apply stored player changes
        if (voxelChanges.ContainsKey(chunkCoord))
        {   // Override with player-made modifications
            foreach (var change in voxelChanges[chunkCoord])
            {
                Vector3Int pos = change.Key;
                Voxel.VoxelType type = change.Value;
                voxels[pos.x, pos.y, pos.z] = VoxelTypeManager.CreateVoxel(new Vector3(pos.x, pos.y, pos.z), type);
            }
        }

        return voxels;
    }

    void FixedUpdate()
    {   // Update chunk loading/unloading based on camera visibility
        if (cameraController == null || Camera.main == null)
            return;

        // Load visible chunks based on camera position
        LoadVisibleChunks();

        // Unload chunks that are no longer visible
        if (lastVisibleChunksFrame != Time.frameCount)
        {   // Process chunk unloading with frame delay to avoid GC allocations
            lastVisibleChunksFrame = Time.frameCount;
            chunksToUnload.Clear();

            foreach (var chunkCoord in chunks.Keys)
            {
                if (!visibleChunks.Contains(chunkCoord) &&
                    chunkLastVisibleFrame.TryGetValue(chunkCoord, out int lastVisibleFrame) &&
                    Time.frameCount - lastVisibleFrame > chunkUnloadDelayFrames)
                {
                    chunksToUnload.Add(chunkCoord);
                }
            }

            for (int i = 0; i < chunksToUnload.Count; i++)
                UnloadChunk(chunksToUnload[i]);
        }
    }

    private void LoadAllChunks()
    {   // Load all chunks in the world size grid
        for (int x = 0; x < worldSize.x; x++)
        {
            for (int y = 0; y < worldSize.y; y++)
            {
                for (int z = 0; z < worldSize.x; z++)
                {
                    Vector3Int chunkCoord = new Vector3Int(x, y, z);
                    if (!chunks.ContainsKey(chunkCoord))
                        LoadChunk(chunkCoord);
                    else if (chunks[chunkCoord] != null && !chunks[chunkCoord].gameObject.activeSelf)
                        chunks[chunkCoord].gameObject.SetActive(true);
                }
            }
        }
    }


    private HashSet<Vector3Int> GetActualVisibleChunks(Camera cam, Vector3Int cameraChunk, Vector3 cameraPos)
    {   // Get visible chunks using frustum culling and simple distance check
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();
        float maxDistance = chunkSize * Mathf.Max(worldSize.x, worldSize.y) * 1.5f;
        
        for (int x = 0; x < worldSize.x; x++)
        {
            for (int y = 0; y < worldSize.y; y++)
            {
                for (int z = 0; z < worldSize.x; z++)
                {
                    Vector3Int chunkCoord = new Vector3Int(x, y, z);
                    Vector3 chunkCenter = ChunkCoordToWorldPos(chunkCoord) + Vector3.one * (chunkSize / 2f);
                    Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);

                    // Use frustum culling and distance check instead of complex ray casting
                    if (GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, chunkBounds))
                    {
                        float distanceToCamera = Vector3.Distance(cameraPos, chunkCenter);
                        if (distanceToCamera <= maxDistance)
                            result.Add(chunkCoord);
                    }
                }
            }
        }
        return result;
    }

    // Ray-based visibility check to determine if chunk is actually visible
    private bool IsChunkVisibleByRay(Vector3 cameraPos, Vector3Int chunkCoord, Vector3 chunkCenter)
    {   // Test multiple points on chunk faces for visibility
        Vector3[] checkPoints = GetChunkVisibilityPoints(chunkCoord);
        foreach (Vector3 point in checkPoints)
        {
            Vector3 dir = (point - cameraPos).normalized;
            float distanceToPoint = Vector3.Distance(cameraPos, point);
            Ray ray = new Ray(cameraPos, dir);
            if (!IsRayBlockedByOtherChunks(ray, distanceToPoint, chunkCoord))
                return true;
        }
        return false; // No points are visible
    }

    // Get key points on chunk faces for visibility testing
    private Vector3[] GetChunkVisibilityPoints(Vector3Int chunkCoord)
    {   // Calculate face center points for visibility testing
        Vector3 chunkWorldPos = ChunkCoordToWorldPos(chunkCoord);
        float halfSize = chunkSize / 2f;
        Vector3 center = chunkWorldPos + Vector3.one * halfSize;
        return new Vector3[]
        {
            center + Vector3.left * halfSize,
            center + Vector3.right * halfSize,
            center + Vector3.down * halfSize,
            center + Vector3.up * halfSize,
            center + Vector3.back * halfSize,
            center + Vector3.forward * halfSize,
            center
        };
    }

    // Check if ray is blocked by other loaded chunks
    private bool IsRayBlockedByOtherChunks(Ray ray, float maxDistance, Vector3Int targetChunkCoord)
    {   // Test ray intersection against all other chunk bounds
        foreach (var kvp in chunks)
        {
            var chunkCoord = kvp.Key;
            if (chunkCoord == targetChunkCoord)
                continue;

            Bounds chunkBounds = new Bounds(ChunkCoordToWorldPos(chunkCoord) + Vector3.one * (chunkSize / 2f), Vector3.one * chunkSize);

            if (chunkBounds.IntersectRay(ray, out float distance))
            {
                if (distance < maxDistance)
                    return true;
            }
        }
        return false; // Ray is not blocked
    }

    private void LoadChunk(Vector3Int chunkCoord)
    {   // Create and initialize a new chunk at the specified coordinates
        Vector3 chunkWorldPos = ChunkCoordToWorldPos(chunkCoord);
        GameObject chunkObj = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}");
        chunkObj.transform.position = chunkWorldPos;
        chunkObj.transform.parent = transform;
        Chunk chunk = chunkObj.AddComponent<Chunk>();
        
        // Initialize chunk with pre-generated voxel data if available
        if (chunkVoxelData.ContainsKey(chunkCoord))
            chunk.InitializeWithVoxelData(chunkSize, chunkVoxelData[chunkCoord]);
        else
            chunk.Initialize(chunkSize);
            
        chunks[chunkCoord] = chunk;
    }

    private void UnloadChunk(Vector3Int chunkPos)
    {   // Remove and destroy chunk at specified position
        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            if (chunk != null && chunk.gameObject != null)
                Destroy(chunk.gameObject);
            chunks.Remove(chunkPos);
            chunkLastVisibleFrame.Remove(chunkPos);
        }
    }

    private void LoadVisibleChunks()
    {   // Load chunks visible to the camera and update visibility tracking
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 cameraPos = cam.transform.position;
        Vector3Int cameraChunk = GetChunkOrigin(cameraPos) / chunkSize;
        cameraFrustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
        HashSet<Vector3Int> visibleChunkCoords = GetActualVisibleChunks(cam, cameraChunk, cameraPos);

        foreach (var chunkCoord in visibleChunkCoords)
        {
            if (!chunks.ContainsKey(chunkCoord))
                LoadChunk(chunkCoord);
            chunkLastVisibleFrame[chunkCoord] = Time.frameCount;
        }

        visibleChunks = visibleChunkCoords;
        lastCameraChunk = cameraChunk;
    }

    // Centralized chunk position calculation for consistency and speed
    private Vector3Int ChunkCoordToWorldPos(Vector3Int chunkCoord) =>
        worldOrigin + new Vector3Int(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);

    public Vector3Int GetChunkOrigin(Vector3 position)
    {   // Calculate chunk coordinates from world position
        int x = Mathf.FloorToInt((position.x - worldOrigin.x) / chunkSize);
        int y = Mathf.FloorToInt((position.y - worldOrigin.y) / chunkSize);
        int z = Mathf.FloorToInt((position.z - worldOrigin.z) / chunkSize);
        return new Vector3Int(x, y, z);
    }

    public Vector3Int GetChunkOrigin(Vector3Int position)
    {   // Calculate chunk coordinates from integer world position
        int x = Mathf.FloorToInt((position.x - worldOrigin.x) / chunkSize);
        int y = Mathf.FloorToInt((position.y - worldOrigin.y) / chunkSize);
        int z = Mathf.FloorToInt((position.z - worldOrigin.z) / chunkSize);
        return new Vector3Int(x, y, z);
    }

    // Returns the chunk at a given world position, or null if not loaded
    public Chunk GetChunk(Vector3 globalPosition)
    {   // Retrieve chunk from world position if loaded
        Vector3Int chunkCoord = GetChunkOrigin(globalPosition);
        if (chunks.TryGetValue(chunkCoord, out Chunk chunk))
            return chunk;
        return null;
    }

    // Returns the voxel type at a given world position
    public Voxel.VoxelType DetermineVoxelType(float x, float y, float z)
    {   // Determine voxel type based on terrain height and noise
        int cSize = chunkSize;
        float noiseValue = GlobalNoise.GetGlobalNoiseValue(x, z, noiseArray);
        float normalizedNoiseValue = (noiseValue + 1) * 0.5f;
        float terrainHeight = normalizedNoiseValue * maxHeight * cSize;
        return y <= terrainHeight ? Voxel.VoxelType.Grass : Voxel.VoxelType.Air;
    }

    private Voxel.VoxelType DetermineLayeredVoxelType(float y, float x, float z)
    {   // Create rectangular concentric layers from center to outside edges
        Vector3 worldMin = GetWorldMin();
        Vector3 worldMax = GetWorldMax();
        Vector3 worldCenter = (worldMin + worldMax) / 2f;
        Vector3 voxelPos = new Vector3(x, y, z);
        
        // Calculate distance to nearest edge in each dimension
        float distanceToEdgeX = Mathf.Min(voxelPos.x - worldMin.x, worldMax.x - voxelPos.x);
        float distanceToEdgeY = Mathf.Min(voxelPos.y - worldMin.y, worldMax.y - voxelPos.y);
        float distanceToEdgeZ = Mathf.Min(voxelPos.z - worldMin.z, worldMax.z - voxelPos.z);
        
        // Use minimum distance to any edge to determine layer
        float minDistanceToEdge = Mathf.Min(distanceToEdgeX, Mathf.Min(distanceToEdgeY, distanceToEdgeZ));
        float maxPossibleDistance = GetMaxDistanceToCenter();
        
        // Calculate layer index based on distance from edge
        int layerIndex = Mathf.FloorToInt((minDistanceToEdge / maxPossibleDistance) * GetVoxelTypeCount());
        return GetVoxelTypeByLayer(layerIndex);
    }

    private Vector3 GetWorldMin() =>
        new Vector3(worldOrigin.x, worldOrigin.y, worldOrigin.z);

    private Vector3 GetWorldMax() =>
        new Vector3(worldOrigin.x + worldSize.x * chunkSize, 
                   worldOrigin.y + worldSize.y * chunkSize, 
                   worldOrigin.z + worldSize.x * chunkSize);

    private float GetMaxDistanceToCenter()
    {   // Calculate maximum distance from center to any edge
        float maxX = (worldSize.x * chunkSize) / 2f;
        float maxY = (worldSize.y * chunkSize) / 2f;
        float maxZ = (worldSize.x * chunkSize) / 2f;
        return Mathf.Min(maxX, Mathf.Min(maxY, maxZ));
    }

    private int GetVoxelTypeCount() => 7;  // Total number of non-air voxel types

    private Voxel.VoxelType GetVoxelTypeByLayer(int layerIndex)
    {   // Return voxel type based on layer index from center outward
        return layerIndex switch
        {
            0 => Voxel.VoxelType.Yellow,   // Center - brightest
            1 => Voxel.VoxelType.Orange,   // Inner layer
            2 => Voxel.VoxelType.Pink,     // Mid-inner layer
            3 => Voxel.VoxelType.Purple,   // Middle layer
            4 => Voxel.VoxelType.Cyan,     // Mid-outer layer
            5 => Voxel.VoxelType.Stone,    // Outer layer
            _ => Voxel.VoxelType.Grass     // Outermost layer
        };
    }

    public void StoreVoxelChange(Chunk chunk, int x, int y, int z, Voxel.VoxelType newType)
    {   // Store player-made voxel changes for persistence across chunk loading/unloading
        Vector3Int chunkCoord = GetChunkCoordinateFromChunk(chunk);
        Vector3Int voxelCoord = new Vector3Int(x, y, z);

        if (!voxelChanges.ContainsKey(chunkCoord))
            voxelChanges[chunkCoord] = new Dictionary<Vector3Int, Voxel.VoxelType>();

        voxelChanges[chunkCoord][voxelCoord] = newType;

        // Also update the stored chunk data
        if (chunkVoxelData.ContainsKey(chunkCoord))
            chunkVoxelData[chunkCoord][x, y, z] = VoxelTypeManager.CreateVoxel(new Vector3(x, y, z), newType);
    }

    private Vector3Int GetChunkCoordinateFromChunk(Chunk chunk)
    {   // Find chunk coordinate from chunk instance
        return chunks.FirstOrDefault(kvp => kvp.Value == chunk).Key;
    }
}