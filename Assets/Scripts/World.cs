using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class World : MonoBehaviour
{
    public Vector2Int worldSize = new Vector2Int(5, 3); // World size in chunks (XZ, Y)
    public int chunkSize = 16; // Size of each chunk
    public int loadDistance = 3; // Chunks within this distance are preloaded

    // Use Vector3Int for chunk keys for better hashing and performance
    private Dictionary<Vector3Int, Chunk> chunks;
    public static World Instance { get; private set; }
    public Material VoxelMaterial;

    // noise
    public int noiseSeed = 1234;
    public float maxHeight = 0.2f;
    public float noiseScale = 0.015f;
    public float[,] noiseArray;

    private int totalChunks;
    private Vector3Int[] chunkPositionsCache;    // Ray-based chunk loading
    private Queue<Vector3Int> chunkGenerationQueue = new Queue<Vector3Int>();
    private HashSet<Vector3Int> chunksToUpdate = new HashSet<Vector3Int>();
    private bool isBatchUpdating = false;

    private Vector3Int lastCameraChunk = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

    private const int chunkCacheSize = 32; // Max number of cached chunks
    private HashSet<Vector3Int> visibleChunks = new HashSet<Vector3Int>();
    private Plane[] cameraFrustumPlanes;

    // Debug/Performance tracking
    private int chunksLoadedThisFrame = 0;
    private int totalChunksLoaded = 0;
    private float lastDebugTime = 0f;

    // Use a HashSet to avoid duplicate chunk positions in the queue
    private HashSet<Vector3Int> chunkQueueSet = new HashSet<Vector3Int>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
        noiseArray = GlobalNoise.GetNoise();
    }

    void Start()
    {
        chunks = new Dictionary<Vector3Int, Chunk>(worldSize.x * worldSize.x * worldSize.y);
        PrecomputeChunkPositions();
        EnqueueInitialChunks();
        StartCoroutine(GenerateWorldCoroutine());
    }

    void FixedUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3Int cameraChunk = GetChunkOrigin(cam.transform.position) / chunkSize;
        Vector3 cameraPos = cam.transform.position;

        // Preload all chunks within load distance
        HashSet<Vector3Int> chunksToKeepLoaded = new HashSet<Vector3Int>();
        for (int x = -loadDistance; x <= loadDistance; x++)
        for (int y = -loadDistance; y <= loadDistance; y++)
        for (int z = -loadDistance; z <= loadDistance; z++)
        {
            Vector3Int chunkCoord = cameraChunk + new Vector3Int(x, y, z);
            if (chunkCoord.x < 0 || chunkCoord.x >= worldSize.x ||
                chunkCoord.y < 0 || chunkCoord.y >= worldSize.y ||
                chunkCoord.z < 0 || chunkCoord.z >= worldSize.x)
                continue;
            Vector3Int chunkPos = new Vector3Int(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);
            chunksToKeepLoaded.Add(chunkPos);
            if (!chunks.ContainsKey(chunkPos) && !chunkQueueSet.Contains(chunkPos)) {
                chunkGenerationQueue.Enqueue(chunkPos);
                chunkQueueSet.Add(chunkPos);
            }
        }

        // Unload chunks outside load distance
        foreach (var kvp in new List<KeyValuePair<Vector3Int, Chunk>>(chunks))
        {
            if (!chunksToKeepLoaded.Contains(kvp.Key))
                UnloadChunk(kvp.Key);
        }

        // Only update visible chunks if camera moved significantly or to a different chunk
        float cameraMoveDistance = Vector3.Distance(cameraPos, lastCameraChunk * chunkSize);
        bool cameraMovedSignificantly = cameraMoveDistance > chunkSize * 0.5f;
        if (cameraChunk != lastCameraChunk || cameraMovedSignificantly)
        {
            cameraFrustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            HashSet<Vector3Int> newVisibleChunks = GetActualVisibleChunks(cam, cameraChunk);
            foreach (var kvp in chunks)
            {
                Chunk chunk = kvp.Value;
                Vector3Int chunkCoord = new Vector3Int(kvp.Key.x / chunkSize, kvp.Key.y / chunkSize, kvp.Key.z / chunkSize);
                bool isVisible = newVisibleChunks.Contains(chunkCoord);
                if (chunk != null && chunk.gameObject != null && chunk.gameObject.activeSelf != isVisible)
                    chunk.gameObject.SetActive(isVisible);
            }
            visibleChunks = newVisibleChunks;
            lastCameraChunk = cameraChunk;
        }
        if (chunkGenerationQueue.Count > 0 && !isBatchUpdating)
            StartCoroutine(GenerateWorldCoroutine());
    }    // Get only visible chunks using ray-based visibility detection
    private HashSet<Vector3Int> GetActualVisibleChunks(Camera cam, Vector3Int cameraChunk)
    {
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();
        Vector3 cameraPos = cam.transform.position;
        
        // Check all chunks within the world bounds for visibility
        for (int x = 0; x < worldSize.x; x++)
        for (int y = 0; y < worldSize.y; y++)
        for (int z = 0; z < worldSize.x; z++)
        {
            Vector3Int chunkCoord = new Vector3Int(x, y, z);
            Vector3 chunkWorldPos = new Vector3(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);
            Vector3 chunkCenter = chunkWorldPos + Vector3.one * (chunkSize / 2f);
            
            // Check frustum culling first
            Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);
            if (!GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, chunkBounds)) continue;
            
            // Use ray-based visibility check
            if (IsChunkVisibleByRay(cameraPos, chunkCoord, chunkCenter))
                result.Add(chunkCoord);
        }
        return result;
    }

    // Ray-based visibility check to determine if chunk is actually visible
    private bool IsChunkVisibleByRay(Vector3 cameraPos, Vector3Int chunkCoord, Vector3 chunkCenter)
    {
        // Check multiple points on the chunk faces to ensure visibility
        Vector3[] checkPoints = GetChunkVisibilityPoints(chunkCoord);
        
        foreach (Vector3 point in checkPoints)
        {
            Vector3 directionToPoint = (point - cameraPos).normalized;
            float distanceToPoint = Vector3.Distance(cameraPos, point);
            
            // Cast ray from camera to chunk point
            Ray ray = new Ray(cameraPos, directionToPoint);
            
            // Check if ray hits any other chunk before reaching this point
            if (!IsRayBlockedByOtherChunks(ray, distanceToPoint, chunkCoord))
                return true; // At least one point is visible
        }
        
        return false; // No points are visible
    }

    // Get key points on chunk faces for visibility testing
    private Vector3[] GetChunkVisibilityPoints(Vector3Int chunkCoord)
    {
        Vector3 chunkWorldPos = new Vector3(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);
        float halfSize = chunkSize / 2f;
        Vector3 center = chunkWorldPos + Vector3.one * halfSize;
        
        // Test center of each face
        return new Vector3[]
        {
            center + Vector3.left * halfSize,     // -X face center
            center + Vector3.right * halfSize,    // +X face center
            center + Vector3.down * halfSize,     // -Y face center
            center + Vector3.up * halfSize,       // +Y face center
            center + Vector3.back * halfSize,     // -Z face center
            center + Vector3.forward * halfSize,  // +Z face center
            center                                // Chunk center
        };
    }

    // Check if ray is blocked by other loaded chunks
    private bool IsRayBlockedByOtherChunks(Ray ray, float maxDistance, Vector3Int targetChunkCoord)
    {
        foreach (var kvp in chunks)
        {
            Vector3Int chunkPos = kvp.Key;
            Vector3Int chunkCoord = new Vector3Int(chunkPos.x / chunkSize, chunkPos.y / chunkSize, chunkPos.z / chunkSize);
            
            // Skip the target chunk itself
            if (chunkCoord == targetChunkCoord) continue;
            
            // Create bounds for this chunk
            Vector3 chunkCenter = new Vector3(chunkPos.x + chunkSize/2f, chunkPos.y + chunkSize/2f, chunkPos.z + chunkSize/2f);
            Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);
            
            // Check if ray intersects this chunk's bounds
            if (chunkBounds.IntersectRay(ray, out float distance))
            {
                if (distance < maxDistance)
                    return true; // Ray is blocked
            }
        }
        
        return false; // Ray is not blocked
    }// Check if chunk has any faces that are actually visible to the camera
    private bool HasAnyVisibleFace(Vector3Int chunkCoord, Vector3 cameraPos)
    {
        Vector3 chunkCenter = new Vector3(chunkCoord.x * chunkSize + chunkSize/2f, chunkCoord.y * chunkSize + chunkSize/2f, chunkCoord.z * chunkSize + chunkSize/2f);
        Vector3 toCameraDir = (cameraPos - chunkCenter).normalized;
        
        // Define face normals and their corresponding neighbor offsets
        Vector3[] faceNormals = {
            Vector3.left,    // -X face
            Vector3.right,   // +X face
            Vector3.down,    // -Y face
            Vector3.up,      // +Y face
            Vector3.back,    // -Z face
            Vector3.forward  // +Z face
        };
        
        Vector3Int[] neighborOffsets = {
            new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1)
        };
        
        for (int i = 0; i < faceNormals.Length; i++)
        {
            Vector3 faceNormal = faceNormals[i];
            Vector3Int neighborCoord = chunkCoord + neighborOffsets[i];
            Vector3Int neighborWorldPos = new Vector3Int(neighborCoord.x * chunkSize, neighborCoord.y * chunkSize, neighborCoord.z * chunkSize);
            
            // Face is potentially visible if:
            // 1. Camera is on the side of the face (dot product > 0.1 for some tolerance)
            // 2. No neighboring chunk exists to occlude this face OR neighbor is out of world bounds
            bool cameraFacingFace = Vector3.Dot(faceNormal, toCameraDir) > 0.1f;
            bool neighborExists = chunks.ContainsKey(neighborWorldPos);
            bool neighborOutOfBounds = neighborCoord.x < 0 || neighborCoord.x >= worldSize.x || 
                                     neighborCoord.y < 0 || neighborCoord.y >= worldSize.y ||
                                     neighborCoord.z < 0 || neighborCoord.z >= worldSize.x;
            
            if (cameraFacingFace && (!neighborExists || neighborOutOfBounds))
                return true;
        }
        
        return false;
    }

    private void UnloadChunk(Vector3Int chunkPos)
    {
        if (chunks.TryGetValue(chunkPos, out Chunk chunk))
        {
            GameObject chunkObj = chunk.gameObject;
            chunks.Remove(chunkPos);
            Destroy(chunkObj);
        }
    }

    private GameObject GetChunkFromCacheOrNew(Vector3Int chunkPosition)
    {
        // Always create a new GameObject now
        return new GameObject($"Chunk_{chunkPosition.x}_{chunkPosition.y}_{chunkPosition.z}");
    }    private void EnqueueInitialChunks()
    {
        // Only enqueue chunks that would be visible from the initial camera position
        Camera cam = Camera.main;
        if (cam != null)
        {
            cameraFrustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            Vector3Int cameraChunk = GetChunkOrigin(cam.transform.position) / chunkSize;
            HashSet<Vector3Int> initialVisibleChunks = GetActualVisibleChunks(cam, cameraChunk);
            
            // Enqueue visible chunks without distance sorting
            foreach (var chunkCoord in initialVisibleChunks)
            {
                Vector3Int chunkPos = new Vector3Int(chunkCoord.x * chunkSize, chunkCoord.y * chunkSize, chunkCoord.z * chunkSize);
                chunkGenerationQueue.Enqueue(chunkPos);
            }
        }
        else
        {
            // Fallback if no camera is available at start - load only visible chunks from origin
            for (int i = 0; i < totalChunks; i++)
            {
                chunkGenerationQueue.Enqueue(chunkPositionsCache[i]);
            }
        }
    }// Coroutine-based chunk generation for large worlds (Unity main thread safe)
    private System.Collections.IEnumerator GenerateWorldCoroutine()
    {
        int batchSize = 8;
        int processed = 0;
        chunksLoadedThisFrame = 0;
        while (chunkGenerationQueue.Count > 0)
        {
            Vector3Int chunkPosition = chunkGenerationQueue.Dequeue();
            chunkQueueSet.Remove(chunkPosition);
            if (!chunks.ContainsKey(chunkPosition))
            {
                GameObject chunkObject = GetChunkFromCacheOrNew(chunkPosition);
                chunkObject.transform.position = chunkPosition;
                Chunk chunk = chunkObject.GetComponent<Chunk>() ?? chunkObject.AddComponent<Chunk>();
                chunk.Initialize(chunkSize);
                chunks[chunkPosition] = chunk;
                QueueChunkForUpdate(chunkPosition);
                chunksLoadedThisFrame++;
                totalChunksLoaded++;
            }
            processed++;
            if (processed >= batchSize)
            {
                processed = 0;
                yield return null;
            }
        }
        if (Time.time - lastDebugTime > 2f)
        {
            Debug.Log($"Chunks loaded this batch: {chunksLoadedThisFrame}, Total chunks: {chunks.Count}, Queue size: {chunkGenerationQueue.Count}");
            lastDebugTime = Time.time;
        }
        StartCoroutine(BatchUpdateChunkMeshes());
    }

    private void QueueChunkForUpdate(Vector3Int chunkPosition)
    {
        chunksToUpdate.Add(chunkPosition);
        Vector3Int[] directions = {
            new Vector3Int(-1,0,0), new Vector3Int(1,0,0),
            new Vector3Int(0,-1,0), new Vector3Int(0,1,0),
            new Vector3Int(0,0,-1), new Vector3Int(0,0,1)
        };
        foreach (var dir in directions)
        {
            Vector3Int neighborPos = chunkPosition + dir * chunkSize;
            if (chunks.ContainsKey(neighborPos))
                chunksToUpdate.Add(neighborPos);
        }
    }

    private System.Collections.IEnumerator BatchUpdateChunkMeshes()
    {
        if (isBatchUpdating) yield break;
        isBatchUpdating = true;
        int batchSize = 4;
        int processed = 0;
        var toUpdate = new List<Vector3Int>(chunksToUpdate);
        chunksToUpdate.Clear();
        foreach (var pos in toUpdate)
        {
            if (chunks.TryGetValue(pos, out Chunk chunk))
                chunk.GenerateMesh();
            processed++;
            if (processed >= batchSize)
            {
                processed = 0;
                yield return null;
            }
        }
        isBatchUpdating = false;
    }

    public Voxel.VoxelType DetermineVoxelType(float x, float y, float z)
    {
        // Use local variable for chunkSize to avoid repeated property access
        int cSize = chunkSize;
        float noiseValue = GlobalNoise.GetGlobalNoiseValue(x, z, noiseArray);
        float normalizedNoiseValue = (noiseValue + 1) * 0.5f;
        float terrainHeight = normalizedNoiseValue * maxHeight * cSize;
        return y <= terrainHeight ? Voxel.VoxelType.Grass : Voxel.VoxelType.Air;
    }

    public Chunk GetChunk(Vector3 globalPosition)
    {
        Vector3Int chunkPosition = GetChunkOrigin(globalPosition);
        chunks.TryGetValue(chunkPosition, out Chunk chunk);
        return chunk;
    }

    public IEnumerable<Chunk> GetAllChunks()
    {
        return chunks.Values;
    }

    // Call this when a voxel is updated to update faces on this and neighbor chunks
    public void UpdateVoxelAndNeighbors(Vector3Int voxelPosition)
    {
        Vector3Int chunkOrigin = GetChunkOrigin(voxelPosition);
        QueueChunkForUpdate(chunkOrigin);
        Vector3Int[] directions = {
            new Vector3Int(-1,0,0), new Vector3Int(1,0,0),
            new Vector3Int(0,-1,0), new Vector3Int(0,1,0),
            new Vector3Int(0,0,-1), new Vector3Int(0,0,1)
        };
        foreach (var dir in directions)
        {
            Vector3Int neighborVoxel = voxelPosition + dir * chunkSize;
            Vector3Int neighborChunkOrigin = GetChunkOrigin(neighborVoxel);
            if (neighborChunkOrigin != chunkOrigin)
                QueueChunkForUpdate(neighborChunkOrigin);
        }
        // Start batch update if not already running
        if (!isBatchUpdating)
            StartCoroutine(BatchUpdateChunkMeshes());
    }

    // Centralized chunk position calculation for consistency and speed
    public Vector3Int GetChunkOrigin(Vector3 position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / chunkSize) * chunkSize,
            Mathf.FloorToInt(position.y / chunkSize) * chunkSize,
            Mathf.FloorToInt(position.z / chunkSize) * chunkSize
        );
    }

    // Overload for Vector3Int
    public Vector3Int GetChunkOrigin(Vector3Int position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / chunkSize) * chunkSize,
            Mathf.FloorToInt(position.y / chunkSize) * chunkSize,
            Mathf.FloorToInt(position.z / chunkSize) * chunkSize
        );
    }

    private void PrecomputeChunkPositions()
    {
        totalChunks = worldSize.x * worldSize.x * worldSize.y;
        chunkPositionsCache = new Vector3Int[totalChunks];
        for (int i = 0; i < totalChunks; i++)
        {
            int x = i % worldSize.x;
            int y = (i / (worldSize.x * worldSize.x)) % worldSize.y;
            int z = (i / worldSize.x) % worldSize.x;
            chunkPositionsCache[i] = new Vector3Int(x * chunkSize, y * chunkSize, z * chunkSize);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(World))]
    public class WorldEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            World world = (World)target;
            if (GUILayout.Button("Regenerate Faces"))
            {
                world.RegenerateWorld();
            }
        }
    }
#endif

    [ContextMenu("Regenerate World")]
    public void RegenerateWorld()
    {
        foreach (var chunk in GetAllChunks())
        {
            if (chunk != null)
                chunk.GenerateMesh();
        }
    }
}