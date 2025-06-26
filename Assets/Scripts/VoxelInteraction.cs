using UnityEngine;
using System.Threading.Tasks;
using System.Collections.Concurrent;

public class VoxelInteraction : MonoBehaviour
{
    [SerializeField] private float interactionRange = 10f;
    [SerializeField] private Color highlightColor = Color.yellow;
    [SerializeField] private bool showHighlight = true;
    [SerializeField] private float destructionRate = 0.05f; // Time between destructions when holding
    [SerializeField] private int destructionRadius = 1; // Radius of voxels to destroy around target
    [SerializeField] private Color radiusHighlightColor = Color.red;
    [SerializeField] private float holdTimeToActivate = 0.4f; // Time to hold before rapid destruction
    [SerializeField] private int maxRadius = 10; // Maximum destruction radius
    [SerializeField] private float radiusGrowthTime = 0.8f; // Time for radius gizmos to fully grow
    [SerializeField] private Color destructionOverlayColor = Color.red; // Color for destruction overlay
    [SerializeField] private float preDestructionAlpha = 0.3f; // Alpha for voxels about to be destroyed
    
    private Camera playerCamera;
    private Vector3Int highlightedVoxelCoords;
    private bool hasHighlightedVoxel = false;
    private Chunk highlightedChunk;
    private float lastDestructionTime;
    private float mouseHoldStartTime;
    private bool isHoldingMouse = false;
    private bool rapidDestructionActive = false;
    private System.Collections.Generic.List<(Chunk chunk, Vector3Int coords)> voxelsInRadius = new System.Collections.Generic.List<(Chunk, Vector3Int)>();
    private System.Collections.Generic.Queue<(Chunk chunk, Vector3Int coords)> destructionQueue = new System.Collections.Generic.Queue<(Chunk, Vector3Int)>();
    private System.Collections.Generic.HashSet<(Chunk chunk, Vector3Int coords)> queuedVoxels = new System.Collections.Generic.HashSet<(Chunk, Vector3Int)>();
    private System.Collections.Generic.Dictionary<(Chunk, Vector3Int), float> voxelDestructionTimes = new System.Collections.Generic.Dictionary<(Chunk, Vector3Int), float>();
    private System.Collections.Generic.List<(Chunk chunk, Vector3Int coords)> batchBuffer = new System.Collections.Generic.List<(Chunk, Vector3Int)>();
    [SerializeField] private int voxelsPerFrameDestruction = 16; // Number of voxels to destroy per frame
    [SerializeField] private bool useBatchProcessing = true; // Enable batch processing for better performance
    [SerializeField] private int batchSize = 8; // Size of each processing batch

    void Start()
    {   // Initialize camera reference for raycasting
        playerCamera = Camera.main;
        if (playerCamera == null)
            playerCamera = FindObjectOfType<Camera>();
    }

    void Update()
    {   // Handle voxel interaction input and highlighting
        ProcessDestructionQueue();
        UpdateHighlightedVoxel();
        HandleMouseInput();
        HandleRadiusInput();
        ProcessDestructionQueue();
    }

    private void UpdateHighlightedVoxel()
    {   // Update which voxel is currently being targeted by the mouse cursor
        voxelsInRadius.Clear();
        
        if (playerCamera == null)
            return;

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {   // Process hit against chunk mesh to find target voxel
            Chunk hitChunk = hit.collider.GetComponent<Chunk>();
            if (hitChunk != null)
            {   // Use chunk size dynamically
                int chunkSize = GetChunkSize(hitChunk);
                Vector3 localHitPoint = hitChunk.transform.InverseTransformPoint(hit.point);
                Vector3 localNormal = hitChunk.transform.InverseTransformDirection(hit.normal);
                
                // Move into the voxel from the hit surface
                Vector3 targetPoint = localHitPoint - localNormal * 0.5f;
                
                // Simple floor calculation for voxel coordinates
                Vector3Int voxelCoords = new Vector3Int(
                    Mathf.FloorToInt(targetPoint.x),
                    Mathf.FloorToInt(targetPoint.y),
                    Mathf.FloorToInt(targetPoint.z)
                );
                
                // Clamp to chunk bounds
                voxelCoords.x = Mathf.Clamp(voxelCoords.x, 0, chunkSize - 1);
                voxelCoords.y = Mathf.Clamp(voxelCoords.y, 0, chunkSize - 1);
                voxelCoords.z = Mathf.Clamp(voxelCoords.z, 0, chunkSize - 1);
                
                if (IsWithinChunkBounds(voxelCoords, chunkSize))
                {   // Verify the target voxel is solid (not air and active)
                    var voxel = hitChunk.GetVoxelAt(voxelCoords.x, voxelCoords.y, voxelCoords.z);
                    if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
                    {   // Store valid voxel coordinates for highlighting and destruction
                        highlightedVoxelCoords = voxelCoords;
                        highlightedChunk = hitChunk;
                        hasHighlightedVoxel = true;
                        
                        // Find all voxels within destruction radius
                        FindVoxelsInRadius(hitChunk, voxelCoords, chunkSize);
                    }
                    else
                        hasHighlightedVoxel = false;
                }
                else
                    hasHighlightedVoxel = false;
            }
            else
                hasHighlightedVoxel = false;
        }
        else
            hasHighlightedVoxel = false;
    }

    private void HandleVoxelDestruction()
    {   // Queue all voxels within the destruction radius for destruction using compute shader when possible
        if (!hasHighlightedVoxel || voxelsInRadius.Count == 0)
            return;

        // Try to use compute shader for batch destruction
        if (TryComputeShaderDestruction())
            return;

        // Fallback to traditional queuing method
        foreach (var (chunk, coords) in voxelsInRadius)
        {   // Only queue if not already queued and voxel still exists
            var voxelKey = (chunk, coords);
            
            if (!queuedVoxels.Contains(voxelKey) && chunk != null)
            {   // Check if voxel still exists before queuing
                var voxel = chunk.GetVoxelAt(coords.x, coords.y, coords.z);
                if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
                {   // Add to queue only if voxel exists and not already queued
                    destructionQueue.Enqueue(voxelKey);
                    queuedVoxels.Add(voxelKey);
                    voxelDestructionTimes[voxelKey] = Time.time;
                }
            }
        }
            
        lastDestructionTime = Time.time;
    }

    private bool TryComputeShaderDestruction()
    {   // Attempt to use compute shader for faster batch destruction
        if (highlightedChunk == null)
            return false;

        // Convert chunk coordinates to indices for compute shader
        var destructionIndices = new System.Collections.Generic.List<int>();
        int chunkSize = GetChunkSize(highlightedChunk);
        
        foreach (var (chunk, coords) in voxelsInRadius)
        {   // Only process voxels in the same chunk for now
            if (chunk == highlightedChunk)
            {   // Convert 3D coordinates to 1D index
                int index = coords.x + coords.y * chunkSize + coords.z * chunkSize * chunkSize;
                destructionIndices.Add(index);
            }
        }

        if (destructionIndices.Count > 0)
        {   // Use compute shader for destruction
            Vector3 localCenter = highlightedVoxelCoords;
            highlightedChunk.DestroyVoxelsWithComputeShader(localCenter, destructionRadius, destructionIndices.ToArray());
            return true;
        }

        return false;
    }

    private void HandleSingleVoxelDestruction()
    {   // Destroy only the center voxel for single clicks
        if (!hasHighlightedVoxel || highlightedChunk == null)
            return;

        var voxelKey = (highlightedChunk, highlightedVoxelCoords);
        voxelDestructionTimes[voxelKey] = Time.time;
        DestroyVoxelAt(highlightedChunk, highlightedVoxelCoords);
    }

    private void DestroyVoxelAt(Chunk chunk, Vector3Int voxelCoords)
    {   // Destroy voxel at the exact highlighted coordinates
        int x = voxelCoords.x, y = voxelCoords.y, z = voxelCoords.z;
        int chunkSize = GetChunkSize(chunk);
        
        var voxel = chunk.GetVoxelAt(x, y, z);
        if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
        {   // Convert voxel to air and update world state
            chunk.SetVoxelType(x, y, z, Voxel.VoxelType.Air);
            World.Instance.StoreVoxelChange(chunk, x, y, z, Voxel.VoxelType.Air);
            chunk.GenerateMesh();
            UpdateNeighborChunks(chunk, x, y, z, chunkSize);
            hasHighlightedVoxel = false;
        }
    }

    private void UpdateNeighborChunks(Chunk chunk, int x, int y, int z, int chunkSize)
    {   // Regenerate neighboring chunks when voxel is on chunk boundary
        Vector3 chunkWorldPos = chunk.transform.position;
        Vector3Int[] directions = {
            new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1)
        };

        for (int i = 0; i < directions.Length; i++)
        {   // Check if voxel is on chunk edge and update neighbor
            Vector3Int dir = directions[i];
            int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;
            
            if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkSize || nz < 0 || nz >= chunkSize)
            {   // Voxel is on chunk boundary, update neighbor chunk
                Vector3 neighborPos = chunkWorldPos + new Vector3(dir.x * chunkSize, dir.y * chunkSize, dir.z * chunkSize);
                Chunk neighborChunk = World.Instance.GetChunk(neighborPos);
                if (neighborChunk != null)
                    neighborChunk.GenerateMesh();
            }
        }

        // Also update chunks that might be diagonally adjacent if voxel is at corner
        if ((x == 0 || x == chunkSize - 1) && (y == 0 || y == chunkSize - 1))
        {   // Corner voxel - update diagonal neighbors
            Vector3Int[] diagonalDirs = {
                new Vector3Int(x == 0 ? -1 : 1, y == 0 ? -1 : 1, 0),
                new Vector3Int(x == 0 ? -1 : 1, 0, z == 0 ? -1 : 1),
                new Vector3Int(0, y == 0 ? -1 : 1, z == 0 ? -1 : 1)
            };

            foreach (var diagDir in diagonalDirs)
            {
                Vector3 diagNeighborPos = chunkWorldPos + new Vector3(diagDir.x * chunkSize, diagDir.y * chunkSize, diagDir.z * chunkSize);
                Chunk diagNeighborChunk = World.Instance.GetChunk(diagNeighborPos);
                if (diagNeighborChunk != null)
                    diagNeighborChunk.GenerateMesh();
            }
        }
    }

    private int GetChunkSize(Chunk chunk)
    {   // Get the chunk size from the chunk instance
        var sizeField = typeof(Chunk).GetField("chunkSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return sizeField != null ? (int)sizeField.GetValue(chunk) : 16;
    }

    private bool IsWithinChunkBounds(Vector3Int coords, int chunkSize)
    {   // Check if voxel coordinates are within chunk bounds
        return coords.x >= 0 && coords.x < chunkSize &&
               coords.y >= 0 && coords.y < chunkSize &&
               coords.z >= 0 && coords.z < chunkSize;
    }

    private void FindVoxelsInRadius(Chunk centerChunk, Vector3Int centerCoords, int chunkSize)
    {   // Find all solid voxels within circular destruction radius from center point
        for (int x = -destructionRadius; x <= destructionRadius; x++)
        {
            for (int y = -destructionRadius; y <= destructionRadius; y++)
            {
                for (int z = -destructionRadius; z <= destructionRadius; z++)
                {   // Check if voxel is within circular radius
                    float distance = Mathf.Sqrt(x * x + y * y + z * z);
                    if (distance > destructionRadius)
                        continue;
                        
                    // Calculate target voxel coordinates
                    Vector3Int targetCoords = centerCoords + new Vector3Int(x, y, z);
                    Chunk targetChunk = centerChunk;
                    
                    // Handle voxels that cross chunk boundaries
                    Vector3Int adjustedCoords = targetCoords;
                    Vector3 chunkOffset = Vector3.zero;
                    
                    if (targetCoords.x < 0 || targetCoords.x >= chunkSize ||
                        targetCoords.y < 0 || targetCoords.y >= chunkSize ||
                        targetCoords.z < 0 || targetCoords.z >= chunkSize)
                    {   // Voxel is in a neighboring chunk
                        chunkOffset.x = targetCoords.x < 0 ? -chunkSize : (targetCoords.x >= chunkSize ? chunkSize : 0);
                        chunkOffset.y = targetCoords.y < 0 ? -chunkSize : (targetCoords.y >= chunkSize ? chunkSize : 0);
                        chunkOffset.z = targetCoords.z < 0 ? -chunkSize : (targetCoords.z >= chunkSize ? chunkSize : 0);
                        
                        Vector3 neighborPos = centerChunk.transform.position + chunkOffset;
                        targetChunk = World.Instance.GetChunk(neighborPos);
                        
                        adjustedCoords.x = ((targetCoords.x % chunkSize) + chunkSize) % chunkSize;
                        adjustedCoords.y = ((targetCoords.y % chunkSize) + chunkSize) % chunkSize;
                        adjustedCoords.z = ((targetCoords.z % chunkSize) + chunkSize) % chunkSize;
                    }
                    
                    // Check if voxel exists and is solid
                    if (targetChunk != null && IsWithinChunkBounds(adjustedCoords, chunkSize))
                    {   // Add solid voxels to destruction list
                        var voxel = targetChunk.GetVoxelAt(adjustedCoords.x, adjustedCoords.y, adjustedCoords.z);
                        if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
                            voxelsInRadius.Add((targetChunk, adjustedCoords));
                    }
                }
            }
        }
    }

    private void HandleMouseInput()
    {   // Handle mouse click timing and destruction logic
        if (Input.GetMouseButtonDown(0))
        {   // Start tracking mouse hold
            mouseHoldStartTime = Time.time;
            isHoldingMouse = true;
            rapidDestructionActive = false;
        }
        
        if (Input.GetMouseButton(0) && isHoldingMouse && hasHighlightedVoxel)
        {   // Check if we should activate rapid destruction
            float holdDuration = Time.time - mouseHoldStartTime;
            
            if (!rapidDestructionActive && holdDuration >= holdTimeToActivate)
                rapidDestructionActive = true;
            
            // Handle destruction based on mode
            if (rapidDestructionActive && Time.time >= lastDestructionTime + destructionRate)
                HandleVoxelDestruction();
        }
        
        if (Input.GetMouseButtonUp(0))
        {   // Handle single click destruction if not in rapid mode
            if (isHoldingMouse && !rapidDestructionActive && hasHighlightedVoxel)
                HandleSingleVoxelDestruction();
                
            isHoldingMouse = false;
            rapidDestructionActive = false;
        }
    }

    private void HandleRadiusInput()
    {   // Handle up/down arrow keys to change destruction radius
        if (Input.GetKeyDown(KeyCode.UpArrow))
            destructionRadius = Mathf.Min(destructionRadius + 1, maxRadius);
        
        if (Input.GetKeyDown(KeyCode.DownArrow))
            destructionRadius = Mathf.Max(destructionRadius - 1, 0);
    }

    private void ProcessDestructionQueue()
    {   // Process multiple queued voxel destructions per frame using batch processing
        if (destructionQueue.Count == 0)
            return;

        if (useBatchProcessing)
            ProcessBatchedDestruction();
        else
            ProcessSequentialDestruction();
    }

    private void ProcessBatchedDestruction()
    {   // Process voxels in batches for better performance with mesh regeneration
        batchBuffer.Clear();
        var chunksToUpdate = new System.Collections.Generic.HashSet<Chunk>();
        int voxelsProcessed = 0;

        // Fill batch buffer
        while (destructionQueue.Count > 0 && batchBuffer.Count < batchSize && voxelsProcessed < voxelsPerFrameDestruction)
        {   // Collect voxels for batch processing
            var (chunk, coords) = destructionQueue.Dequeue();
            var voxelKey = (chunk, coords);
            
            queuedVoxels.Remove(voxelKey);
            
            if (chunk != null)
            {   // Validate voxel still exists
                var voxel = chunk.GetVoxelAt(coords.x, coords.y, coords.z);
                if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
                {   // Add to batch for processing
                    batchBuffer.Add((chunk, coords));
                    chunksToUpdate.Add(chunk);
                    voxelsProcessed++;
                }
            }
            
            voxelDestructionTimes.Remove(voxelKey);
        }

        // Process entire batch at once
        foreach (var (chunk, coords) in batchBuffer)
        {   // Convert voxels to air without immediate mesh update
            chunk.SetVoxelType(coords.x, coords.y, coords.z, Voxel.VoxelType.Air);
            World.Instance.StoreVoxelChange(chunk, coords.x, coords.y, coords.z, Voxel.VoxelType.Air);
        }

        // Update all affected chunks once after batch processing
        foreach (var chunk in chunksToUpdate)
        {   // Regenerate mesh for each affected chunk
            chunk.GenerateMesh();
            UpdateNeighborChunksForBatch(chunk);
        }
    }

    private void ProcessSequentialDestruction()
    {   // Process voxels one by one for immediate feedback
        int voxelsProcessed = 0;
        
        while (destructionQueue.Count > 0 && voxelsProcessed < voxelsPerFrameDestruction)
        {   // Process individual voxels
            var (chunk, coords) = destructionQueue.Dequeue();
            var voxelKey = (chunk, coords);
            
            queuedVoxels.Remove(voxelKey);
            
            if (chunk == null)
            {
                voxelDestructionTimes.Remove(voxelKey);
                continue;
            }
            
            var voxel = chunk.GetVoxelAt(coords.x, coords.y, coords.z);
            if (voxel.isActive && voxel.type != Voxel.VoxelType.Air)
            {   // Destroy voxel immediately
                DestroyVoxelAt(chunk, coords);
                voxelsProcessed++;
            }
            
            voxelDestructionTimes.Remove(voxelKey);
        }
    }

    private void UpdateNeighborChunksForBatch(Chunk chunk)
    {   // Update neighbor chunks after batch processing (simplified version)
        int chunkSize = GetChunkSize(chunk);
        Vector3 chunkWorldPos = chunk.transform.position;
        Vector3Int[] directions = {
            new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1)
        };

        foreach (var dir in directions)
        {   // Update each neighboring chunk
            Vector3 neighborPos = chunkWorldPos + new Vector3(dir.x * chunkSize, dir.y * chunkSize, dir.z * chunkSize);
            Chunk neighborChunk = World.Instance.GetChunk(neighborPos);
            if (neighborChunk != null)
                neighborChunk.GenerateMesh();
        }
    }

    void OnDrawGizmos()
    {   // Draw highlighted voxel gizmo for visual feedback
        if (!showHighlight)
            return;
            
        // Draw center voxel highlight
        if (hasHighlightedVoxel && highlightedChunk != null)
        {   // Calculate world position of the highlighted voxel
            Vector3 worldVoxelPos = highlightedChunk.transform.TransformPoint(highlightedVoxelCoords);
            
            Gizmos.color = highlightColor;
            Gizmos.DrawWireCube(worldVoxelPos + Vector3.one * 0.5f, Vector3.one);
            Gizmos.color = new Color(highlightColor.r, highlightColor.g, highlightColor.b, 0.3f);
            Gizmos.DrawCube(worldVoxelPos + Vector3.one * 0.5f, Vector3.one * 0.9f);
        }
        
        // Draw radius voxels when holding mouse button
        if (isHoldingMouse && hasHighlightedVoxel && destructionRadius > 0)
        {   // Calculate growth factor and draw radius
            float holdDuration = Time.time - mouseHoldStartTime;
            float growthFactor = Mathf.Clamp01(holdDuration / radiusGrowthTime);
            DrawRadiusVoxels(growthFactor);
        }
        
        // Always draw destruction overlays for queued voxels
        DrawDestructionOverlays();
    }

    private void DrawRadiusVoxels(float growthFactor)
    {   // Draw radius voxels with growth animation
        Gizmos.color = radiusHighlightColor;
        
        foreach (var (chunk, coords) in voxelsInRadius)
        {   // Draw outline for each voxel in destruction radius with growth animation
            if (chunk != null && chunk == highlightedChunk && coords == highlightedVoxelCoords)
                continue; // Skip the center voxel as it's already drawn
                
            if (chunk != null)
            {   // Calculate distance from center for growth effect
                Vector3Int centerCoords = chunk == highlightedChunk ? highlightedVoxelCoords : highlightedVoxelCoords;
                Vector3Int offset = coords - centerCoords;
                float distance = Mathf.Sqrt(offset.x * offset.x + offset.y * offset.y + offset.z * offset.z);
                float normalizedDistance = distance / destructionRadius;
                
                // Only show voxels that should be visible based on growth
                if (normalizedDistance <= growthFactor)
                {   // Draw voxel with scaling based on growth
                    Vector3 worldPos = chunk.transform.TransformPoint(coords);
                    float scale = Mathf.Lerp(0.3f, 0.8f, growthFactor);
                    float alpha = Mathf.Lerp(0.1f, 0.4f, growthFactor);
                    
                    Gizmos.color = new Color(radiusHighlightColor.r, radiusHighlightColor.g, radiusHighlightColor.b, 1f);
                    Gizmos.DrawWireCube(worldPos + Vector3.one * 0.5f, Vector3.one * scale);
                    Gizmos.color = new Color(radiusHighlightColor.r, radiusHighlightColor.g, radiusHighlightColor.b, alpha);
                    Gizmos.DrawCube(worldPos + Vector3.one * 0.5f, Vector3.one * (scale * 0.9f));
                }
            }
        }
    }

    private void DrawDestructionOverlays()
    {   // Draw red overlays on voxels being destroyed
        foreach (var kvp in voxelDestructionTimes)
        {   // Show destruction overlay for all queued voxels
            var (chunk, coords) = kvp.Key;
            
            if (chunk != null)
            {   // Check if voxel still exists (not yet destroyed)
                var voxel = chunk.GetVoxelAt(coords.x, coords.y, coords.z);
                bool voxelExists = voxel.isActive && voxel.type != Voxel.VoxelType.Air;
                
                if (voxelExists)
                {   // Show faded overlay for queued voxels
                    Vector3 worldPos = chunk.transform.TransformPoint(coords);
                    Gizmos.color = new Color(destructionOverlayColor.r, destructionOverlayColor.g, destructionOverlayColor.b, preDestructionAlpha);
                    Gizmos.DrawCube(worldPos + Vector3.one * 0.5f, Vector3.one * 0.95f);
                }
            }
        }
    }
}