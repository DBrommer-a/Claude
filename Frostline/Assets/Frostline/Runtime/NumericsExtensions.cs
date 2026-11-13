using UnityEngine;
using NQuat = System.Numerics.Quaternion;
using NVec3 = System.Numerics.Vector3;

namespace Frostline
{
    /// <summary>Umrechnung zwischen Unity-Typen und den System.Numerics-Typen der testbaren Kerne.</summary>
    public static class NumericsExtensions
    {
        public static NVec3 ToNumerics(this Vector3 v) { return new NVec3(v.x, v.y, v.z); }
        public static NQuat ToNumerics(this Quaternion q) { return new NQuat(q.x, q.y, q.z, q.w); }
        public static Vector3 ToUnity(this NVec3 v) { return new Vector3(v.X, v.Y, v.Z); }
        public static Quaternion ToUnity(this NQuat q) { return new Quaternion(q.X, q.Y, q.Z, q.W); }
    }
}
