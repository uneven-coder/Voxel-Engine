using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class World : MonoBehaviour
{
    public Vector2Int worldSize = new Vector2Int(5, 3); // World size in chunks (XZ, Y)
    public int chunkSize = 16; // Size of each chunk

    // Use Vector3Int for chunk keys for better hashing and performance
    private Dictionary<Vector3Int, Chunk> chunks;
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
        cameraController = FindObjectOfType<CameraController>();
        LoadAllChunks();
    }

    void FixedUpdate()
    {   // Update chunk loading/unloading based on camera visibility
        if (cameraController == null || Camera.main == null)
            return;

        // Load visible chunks based on camera position
        LoadVisibleChunks();

        // Unload chunks that are no longer visible
        if (lastVisibleChunksFrame != Time.frameCount)
        {   // Process chunk unloading with frame delay
            lastVisibleChunksFrame = Time.frameCount;
            foreach (var chunkCoord in new List<Vector3Int>(chunks.Keys))
            {
                if (!visibleChunks.Contains(chunkCoord) && 
                    chunkLastVisibleFrame.TryGetValue(chunkCoord, out int lastVisibleFrame) &&
                    Time.frameCount - lastVisibleFrame > chunkUnloadDelayFrames)
                {
                    UnloadChunk(chunkCoord);
                }
            }
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
    {   // Get only visible chunks using ray-based visibility detection
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();
        for (int x = 0; x < worldSize.x; x++)
        {
            for (int y = 0; y < worldSize.y; y++)
            {
                for (int z = 0; z < worldSize.x; z++)
                {
                    Vector3Int chunkCoord = new Vector3Int(x, y, z);
                    Vector3 chunkCenter = ChunkCoordToWorldPos(chunkCoord) + Vector3.one * (chunkSize / 2f);
                    Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);
                    
                    if (GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, chunkBounds))
                    {
                        if (IsChunkVisibleByRay(cameraPos, chunkCoord, chunkCenter))
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

}