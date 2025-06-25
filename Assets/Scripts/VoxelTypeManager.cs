using System.Collections.Generic;
using UnityEngine;

public static class VoxelTypeManager
{
    private static readonly Dictionary<Voxel.VoxelType, VoxelTypeData> voxelTypes = new Dictionary<Voxel.VoxelType, VoxelTypeData>();

    static VoxelTypeManager()
    {   // Initialize all voxel type definitions with their properties
        InitializeVoxelTypes();
    }

    private static void InitializeVoxelTypes()
    {   // Define properties for each voxel type
        voxelTypes[Voxel.VoxelType.Air] = new VoxelTypeData
        {
            color = Color.clear,
            isActive = false,
            name = "Air"
        };

        voxelTypes[Voxel.VoxelType.Grass] = new VoxelTypeData
        {
            color = Color.green,
            isActive = true,
            name = "Grass"
        };

        voxelTypes[Voxel.VoxelType.Stone] = new VoxelTypeData
        {
            color = Color.gray,
            isActive = true,
            name = "Stone"
        };
    }

    public static Voxel CreateVoxel(Vector3 position, Voxel.VoxelType type)
    {   // Create a voxel with predefined properties based on type
        var typeData = GetVoxelTypeData(type);
        return new Voxel(position, typeData.color, type, typeData.isActive);
    }

    public static Color GetVoxelColor(Voxel.VoxelType type) =>
        GetVoxelTypeData(type).color;

    public static bool IsVoxelActive(Voxel.VoxelType type) =>
        GetVoxelTypeData(type).isActive;

    public static string GetVoxelName(Voxel.VoxelType type) =>
        GetVoxelTypeData(type).name;

    private static VoxelTypeData GetVoxelTypeData(Voxel.VoxelType type)
    {   // Retrieve voxel type data with fallback to default
        return voxelTypes.TryGetValue(type, out VoxelTypeData data) 
            ? data 
            : new VoxelTypeData { color = Color.white, isActive = false, name = "Unknown" };
    }

    private struct VoxelTypeData
    {
        public Color color;
        public bool isActive;
        public string name;
    }
}