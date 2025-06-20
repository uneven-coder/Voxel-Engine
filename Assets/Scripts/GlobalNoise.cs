using UnityEngine;

public static class GlobalNoise {

    public static float[,] GetNoise() {
        // The number of points to generate in the 1st and 2nd dimension
        int width = World.Instance.chunkSize * World.Instance.worldSize.x; 
        int height = World.Instance.chunkSize * World.Instance.worldSize.x; 

        // The scale of the noise. The greater the scale, the denser the noise gets
        float scale = World.Instance.noiseScale;
        float[,] noise = new float[width, height];
        
        // Generate 2D simplex noise
        for (int x = 0; x < width; x++) {
            for (int z = 0; z < height; z++) {
                noise[x, z] = SimplexNoise.Generate(x * scale, z * scale);
            }
        }
      
        return noise;
    }

    public static float GetGlobalNoiseValue(float globalX, float globalZ, float[,] globalNoiseMap) 
    {
        // convert global coordinates to noise map coordinates
        int noiseMapX = (int)globalX % globalNoiseMap.GetLength(0);
        int noiseMapZ = (int)globalZ % globalNoiseMap.GetLength(1);

        // Ensure the indices are within bounds
        if (noiseMapX >= 0 && noiseMapX < globalNoiseMap.GetLength(0) && 
            noiseMapZ >= 0 && noiseMapZ < globalNoiseMap.GetLength(1)) {
            return globalNoiseMap[noiseMapX, noiseMapZ];
        } 
        else 
            return 0;
    }
}