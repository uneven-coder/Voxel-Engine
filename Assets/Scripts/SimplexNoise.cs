using UnityEngine;

public static class SimplexNoise
{
    // Permutation table
    private static readonly int[] perm = {
        151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
        190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,
        77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,
        135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,
        223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,228,
        251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,
        138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
    };
    
    // Doubled permutation to avoid overflow
    private static readonly int[] p = new int[512];
    
    static SimplexNoise()
    {
        for (int i = 0; i < 512; i++)
        {
            p[i] = perm[i & 255];
        }
    }
    
    // Gradient vectors for 3D
    private static readonly int[,] grad3 = {
        {1,1,0},{-1,1,0},{1,-1,0},{-1,-1,0},
        {1,0,1},{-1,0,1},{1,0,-1},{-1,0,-1},
        {0,1,1},{0,-1,1},{0,1,-1},{0,-1,-1}
    };

    // Preallocated arrays for performance
    private static readonly int[] gradBuffer = new int[3];
    private static readonly float[] temp = new float[4]; // for n0, n1, n2, n3
    private static readonly float[] tx = new float[4];
    private static readonly float[] ty = new float[4];
    private static readonly float[] tz = new float[4];
    private static readonly int[] gis = new int[4];

    // Dot product helper (optimized to avoid array allocation)
    private static float Dot(int gi, float x, float y, float z)
    {
        gradBuffer[0] = grad3[gi, 0];
        gradBuffer[1] = grad3[gi, 1];
        gradBuffer[2] = grad3[gi, 2];
        return gradBuffer[0] * x + gradBuffer[1] * y + gradBuffer[2] * z;
    }

    // 3D simplex noise
    public static float Generate(float x, float y, float z)
    {
        for (int idx = 0; idx < 4; idx++) temp[idx] = 0f;

        float F3 = 1.0f / 3.0f;
        float s = (x + y + z) * F3;
        int i = Mathf.FloorToInt(x + s);
        int j = Mathf.FloorToInt(y + s);
        int k = Mathf.FloorToInt(z + s);

        float G3 = 1.0f / 6.0f;
        float t = (i + j + k) * G3;
        float X0 = i - t;
        float Y0 = j - t;
        float Z0 = k - t;
        float x0 = x - X0;
        float y0 = y - Y0;
        float z0 = z - Z0;

        int i1, j1, k1;
        int i2, j2, k2;

        if (x0 >= y0)
        {
            if (y0 >= z0)
            { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0)
            { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else
            { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else
        {
            if (y0 < z0)
            { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
            else if (x0 < z0)
            { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
            else
            { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        }

        tx[0] = x0; ty[0] = y0; tz[0] = z0;
        tx[1] = x0 - i1 + G3; ty[1] = y0 - j1 + G3; tz[1] = z0 - k1 + G3;
        tx[2] = x0 - i2 + 2.0f * G3; ty[2] = y0 - j2 + 2.0f * G3; tz[2] = z0 - k2 + 2.0f * G3;
        tx[3] = x0 - 1.0f + 3.0f * G3; ty[3] = y0 - 1.0f + 3.0f * G3; tz[3] = z0 - 1.0f + 3.0f * G3;

        int ii = i & 255;
        int jj = j & 255;
        int kk = k & 255;
        gis[0] = p[ii + p[jj + p[kk]]] % 12;
        gis[1] = p[ii + i1 + p[jj + j1 + p[kk + k1]]] % 12;
        gis[2] = p[ii + i2 + p[jj + j2 + p[kk + k2]]] % 12;
        gis[3] = p[ii + 1 + p[jj + 1 + p[kk + 1]]] % 12;

        for (int idx = 0; idx < 4; idx++)
        {
            float t_ = 0.6f - tx[idx] * tx[idx] - ty[idx] * ty[idx] - tz[idx] * tz[idx];
            if (t_ >= 0)
            {
                t_ *= t_;
                temp[idx] = t_ * t_ * Dot(gis[idx], tx[idx], ty[idx], tz[idx]);
            }
        }
        return 32.0f * (temp[0] + temp[1] + temp[2] + temp[3]);
    }
    
    // 2D simplex noise
    public static float Generate(float x, float y)
    {
        return Generate(x, y, 0);
    }

    // Fast hash for permutation table (for future optimization)
    public static int FastHash(int x, int y, int z)
    {
        int h = x * 374761393 + y * 668265263 + z * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        return h & 255;
    }
}