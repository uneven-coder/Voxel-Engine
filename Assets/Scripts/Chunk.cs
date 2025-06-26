using System.Collections.Generic;
using UnityEngine;

public class Chunk : MonoBehaviour
{
    private int chunkSize = 16;
    private ComputeShader voxelComputeShader;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Vector3 chunkWorldPosition;
    private Vector3Int chunkCoord;
    private bool initialized = false;

    public void Initialize(int size, ComputeShader computeShader, Vector3Int coord, ComputeShaderVoxelManager.VoxelData[] initialVoxelData)
    {   // Initialize chunk with compute shader and register voxel data in manager
        chunkSize = size;
        voxelComputeShader = computeShader;
        chunkWorldPosition = transform.position;
        chunkCoord = coord;
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshCollider = gameObject.AddComponent<MeshCollider>();
        ComputeShaderVoxelManager.RegisterChunkData(chunkCoord, chunkSize, initialVoxelData);
        initialized = true;
        GenerateMesh();
    }

    public void GenerateMesh()
    {   // Generate mesh using compute shader face culling and mesh data
        if (!initialized || voxelComputeShader == null)
            return;
        ComputeShaderVoxelManager.PerformFaceCulling(chunkCoord, chunkSize, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        var faceData = ComputeShaderVoxelManager.GetFaceData(chunkCoord, chunkSize);
        var visibilityData = ComputeShaderVoxelManager.GetVisibilityData(chunkCoord, chunkSize);
        BuildMeshFromFaceDataMultiMaterial(faceData, visibilityData);
    }

    private void BuildMeshFromFaceDataMultiMaterial(ComputeShaderVoxelManager.FaceData[] faceData, int[] visibilityData)
    {   // Build mesh with submeshes for each voxel type/material
        int voxelTypeCount = System.Enum.GetValues(typeof(Voxel.VoxelType)).Length;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var trianglesByType = new List<int>[voxelTypeCount];
        for (int i = 0; i < voxelTypeCount; i++)
            trianglesByType[i] = new List<int>();
        int vertOffset = 0;
        for (int i = 0; i < faceData.Length; i++)
        {
            if (faceData[i].isVisible == 0)
                continue;
            int matIdx = Mathf.Clamp(faceData[i].materialIndex, 0, voxelTypeCount - 1);
            vertices.Add(faceData[i].v0);
            vertices.Add(faceData[i].v1);
            vertices.Add(faceData[i].v2);
            vertices.Add(faceData[i].v3);
            uvs.Add(faceData[i].uv0);
            uvs.Add(faceData[i].uv1);
            uvs.Add(faceData[i].uv2);
            uvs.Add(faceData[i].uv3);
            trianglesByType[matIdx].Add(vertOffset + 0);
            trianglesByType[matIdx].Add(vertOffset + 1);
            trianglesByType[matIdx].Add(vertOffset + 2);
            trianglesByType[matIdx].Add(vertOffset + 2);
            trianglesByType[matIdx].Add(vertOffset + 3);
            trianglesByType[matIdx].Add(vertOffset + 0);
            vertOffset += 4;
        }
        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = voxelTypeCount;
        for (int i = 0; i < voxelTypeCount; i++)
            mesh.SetTriangles(trianglesByType[i], i);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
        // Assign materials for each voxel type
        var world = World.Instance;
        Material[] mats = new Material[voxelTypeCount];
        for (int i = 0; i < voxelTypeCount; i++)
        {
            Color c = VoxelTypeManager.GetVoxelColor((Voxel.VoxelType)i);
            mats[i] = (world != null && world.VoxelMaterial != null)
                ? new Material(world.VoxelMaterial)
                : new Material(Shader.Find("Standard"));
            mats[i].color = c;
        }
        meshRenderer.materials = mats;
    }

    private void OnDestroy()
    {   // Clean up mesh, collider, and compute shader buffers
        if (meshFilter != null && meshFilter.mesh != null)
        {
            Destroy(meshFilter.mesh);
        }
        if (meshCollider != null && meshCollider.sharedMesh != null)
        {
            Destroy(meshCollider.sharedMesh);
        }
        ComputeShaderVoxelManager.DisposeChunkBuffers(chunkCoord);
    }

    public void DestroyVoxelsWithComputeShader(Vector3 destructionCenter, int destructionRadius, int[] voxelIndices)
    {   // Use compute shader for batch voxel destruction
        if (!initialized || voxelComputeShader == null)
            return;
        ComputeShaderVoxelManager.PerformVoxelDestruction(chunkCoord, chunkSize, destructionCenter, destructionRadius, voxelIndices);
        GenerateMesh();
    }
}