using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct Voxel
{
    public Vector3 position;
    public Color color;
    public bool isActive;
    public VoxelType type;

    public enum VoxelType
    {
        Air,
        Grass,
        Stone
    }

    public Voxel(Vector3 position, Color color, VoxelType type, bool isActive = true)
    {   // Initialize voxel with provided properties
        this.position = position;
        this.isActive = isActive;
        this.color = color;
        this.type = type;
    }
}