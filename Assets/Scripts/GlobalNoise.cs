using UnityEngine;

public static class GlobalNoise {
    public static float[,] GetNoise() {
        int width = World.Instance.chunkSize * World.Instance.worldSize.x;
        int height = World.Instance.chunkSize * World.Instance.worldSize.y; // Use y for vertical world size
        float scale = World.Instance.noiseScale;
        float[,] noise = new float[width, height];
        for (int x = 0; x < width; x++) {
            for (int z = 0; z < height; z++) {
                noise[x, z] = SimplexNoise.Generate(x * scale, z * scale);
            }
        }
        return noise;
    }

    public static float GetGlobalNoiseValue(float globalX, float globalZ, float[,] globalNoiseMap) 
    {
        int noiseMapX = Mathf.Abs((int)globalX % globalNoiseMap.GetLength(0));
        int noiseMapZ = Mathf.Abs((int)globalZ % globalNoiseMap.GetLength(1));
        if (noiseMapX >= 0 && noiseMapX < globalNoiseMap.GetLength(0) && 
            noiseMapZ >= 0 && noiseMapZ < globalNoiseMap.GetLength(1)) {
            return globalNoiseMap[noiseMapX, noiseMapZ];
        } 
        else 
            return 0;
    }
}