using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct Voxel
{
    public Vector3 position;
    // public Vector3 normal;
    public Color color;

    public Voxel(Vector3 position, Color color) // Vector3 normal, 
    {
        this.position = position;
        // this.normal = normal;
        this.color = color;
    }
}

// public class Voxel : MonoBehaviour
// {
//     // Start is called before the first frame update
//     void Start()
//     {

//     }

//     // Update is called once per frame
//     void Update()
//     {

//     }
// }
