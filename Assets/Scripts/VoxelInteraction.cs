using UnityEngine;
using System.Collections.Generic;

public class VoxelInteraction : MonoBehaviour
{
    [SerializeField] private float interactionRange = 10f;
    [SerializeField] private int destructionRadius = 1;
    [SerializeField] private int maxRadius = 10;
    [SerializeField] private float holdTimeToActivate = 0.4f;
    [SerializeField] private int voxelsPerFrameDestruction = 16;
    [SerializeField] private int batchSize = 8;
    private Camera playerCamera;
    private Vector3Int highlightedVoxelCoords;
    private bool hasHighlightedVoxel = false;
    private Chunk highlightedChunk;
    private float mouseHoldStartTime;
    private bool isHoldingMouse = false;
    private bool rapidDestructionActive = false;
    private List<int> voxelsInRadiusIndices = new List<int>();

    void Start()
    {   // Initialize camera reference
        playerCamera = Camera.main;
        if (playerCamera == null)
            playerCamera = FindObjectOfType<Camera>();
    }

    void Update()
    {   // Handle voxel interaction input and highlighting
        UpdateHighlightedVoxel();
        HandleMouseInput();
        HandleRadiusInput();
    }

    private void UpdateHighlightedVoxel()
    {   // Update which voxel is currently being targeted by the mouse cursor
        hasHighlightedVoxel = false;
        voxelsInRadiusIndices.Clear();
        if (playerCamera == null)
            return;
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
        {
            Chunk hitChunk = hit.collider.GetComponent<Chunk>();
            if (hitChunk != null)
            {
                int chunkSize = World.Instance.chunkSize;
                Vector3 localHitPoint = hitChunk.transform.InverseTransformPoint(hit.point);
                Vector3 localNormal = hitChunk.transform.InverseTransformDirection(hit.normal);
                Vector3 targetPoint = localHitPoint - localNormal * 0.5f;
                Vector3Int voxelCoords = new Vector3Int(
                    Mathf.FloorToInt(targetPoint.x),
                    Mathf.FloorToInt(targetPoint.y),
                    Mathf.FloorToInt(targetPoint.z)
                );
                voxelCoords.x = Mathf.Clamp(voxelCoords.x, 0, chunkSize - 1);
                voxelCoords.y = Mathf.Clamp(voxelCoords.y, 0, chunkSize - 1);
                voxelCoords.z = Mathf.Clamp(voxelCoords.z, 0, chunkSize - 1);
                highlightedVoxelCoords = voxelCoords;
                highlightedChunk = hitChunk;
                hasHighlightedVoxel = true;
                FindVoxelsInRadius(voxelCoords, chunkSize);
            }
        }
    }

    private void FindVoxelsInRadius(Vector3Int centerCoords, int chunkSize)
    {   // Find all voxel indices within spherical destruction radius (single chunk only)
        voxelsInRadiusIndices.Clear();
        for (int x = -destructionRadius; x <= destructionRadius; x++)
            for (int y = -destructionRadius; y <= destructionRadius; y++)
                for (int z = -destructionRadius; z <= destructionRadius; z++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y + z * z);
                    if (distance > destructionRadius)
                        continue;
                    Vector3Int coords = centerCoords + new Vector3Int(x, y, z);
                    if (coords.x >= 0 && coords.x < chunkSize && coords.y >= 0 && coords.y < chunkSize && coords.z >= 0 && coords.z < chunkSize)
                    {
                        int index = coords.x + coords.y * chunkSize + coords.z * chunkSize * chunkSize;
                        voxelsInRadiusIndices.Add(index);
                    }
                }
    }

    private void HandleMouseInput()
    {   // Handle mouse click timing and destruction logic
        if (Input.GetMouseButtonDown(0))
        {
            mouseHoldStartTime = Time.time;
            isHoldingMouse = true;
            rapidDestructionActive = false;
        }
        if (Input.GetMouseButton(0) && isHoldingMouse && hasHighlightedVoxel)
        {
            float holdDuration = Time.time - mouseHoldStartTime;
            if (!rapidDestructionActive && holdDuration >= holdTimeToActivate)
                rapidDestructionActive = true;
            if (rapidDestructionActive)
                HandleVoxelDestruction();
        }
        if (Input.GetMouseButtonUp(0))
        {
            if (isHoldingMouse && !rapidDestructionActive && hasHighlightedVoxel)
                HandleSingleVoxelDestruction();
            isHoldingMouse = false;
            rapidDestructionActive = false;
        }
    }

    private void HandleVoxelDestruction()
    {   // Destroy all voxels in radius across multiple chunks
        if (!hasHighlightedVoxel || highlightedChunk == null)
            return;
        Vector3 worldCenter = highlightedChunk.transform.position + new Vector3(highlightedVoxelCoords.x, highlightedVoxelCoords.y, highlightedVoxelCoords.z);
        DestroyVoxelsInRadius(worldCenter, destructionRadius);
    }

    private void HandleSingleVoxelDestruction()
    {   // Destroy only the center voxel
        if (!hasHighlightedVoxel || highlightedChunk == null)
            return;
        Vector3 worldCenter = highlightedChunk.transform.position + new Vector3(highlightedVoxelCoords.x, highlightedVoxelCoords.y, highlightedVoxelCoords.z);
        DestroyVoxelsInRadius(worldCenter, 0);
    }

    private void DestroyVoxelsInRadius(Vector3 worldCenter, int radius)
    {   // Destroy voxels across multiple chunks within spherical radius
        var affectedChunks = new Dictionary<Chunk, List<int>>();
        int chunkSize = World.Instance.chunkSize;
        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y + z * z);
                    if (distance > radius)
                        continue;
                    Vector3 voxelWorldPos = worldCenter + new Vector3(x, y, z);
                    Chunk chunk = World.Instance.GetChunk(voxelWorldPos);
                    if (chunk == null)
                        continue;
                    Vector3 localPos = chunk.transform.InverseTransformPoint(voxelWorldPos);
                    Vector3Int localCoords = new Vector3Int(
                        Mathf.FloorToInt(localPos.x),
                        Mathf.FloorToInt(localPos.y),
                        Mathf.FloorToInt(localPos.z)
                    );
                    if (localCoords.x >= 0 && localCoords.x < chunkSize && 
                        localCoords.y >= 0 && localCoords.y < chunkSize && 
                        localCoords.z >= 0 && localCoords.z < chunkSize)
                    {
                        int index = localCoords.x + localCoords.y * chunkSize + localCoords.z * chunkSize * chunkSize;
                        if (!affectedChunks.ContainsKey(chunk))
                            affectedChunks[chunk] = new List<int>();
                        affectedChunks[chunk].Add(index);
                    }
                }
        foreach (var kvp in affectedChunks)
        {   // Apply destruction to each affected chunk
            Vector3 chunkLocalCenter = kvp.Key.transform.InverseTransformPoint(worldCenter);
            kvp.Key.DestroyVoxelsWithComputeShader(chunkLocalCenter, radius, kvp.Value.ToArray());
        }
    }

    private void HandleRadiusInput()
    {   // Handle up/down arrow keys to change destruction radius
        if (Input.GetKeyDown(KeyCode.UpArrow))
            destructionRadius = Mathf.Min(destructionRadius + 1, maxRadius);
        if (Input.GetKeyDown(KeyCode.DownArrow))
            destructionRadius = Mathf.Max(destructionRadius - 1, 0);
    }
}
