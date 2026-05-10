using System;
using System.Numerics;

namespace CodeX.Games.RE7.RSZ
{
    public class ViaFolder
    {
        public string Name = "";
        public string Tag = "";
        public bool UpdateSelf;
        public bool DrawSelf;
        public bool Modified;
        public string ScenePath = "";

        public static ViaFolder Parse(RszClass c) => new()
        {
            Name       = c.Get<string>("v0") ?? "",
            Tag        = c.Get<string>("v1") ?? "",
            UpdateSelf = c.GetBool("v2"),
            DrawSelf   = c.GetBool("v3"),
            Modified   = c.GetBool("v4"),
            ScenePath  = c.Get<string>("v5") ?? "",
        };
    }

    public class ViaGameObject
    {
        public string Name = "";
        public string Tag = "";
        public bool UpdateSelf;
        public bool DrawSelf;
        public float TimeScale;

        public static ViaGameObject Parse(RszClass c) => new()
        {
            Name       = c.Get<string>("v0") ?? "",
            Tag        = c.Get<string>("v1") ?? "",
            UpdateSelf = c.GetBool("v2"),
            DrawSelf   = c.GetBool("v3"),
            TimeScale  = c.GetFloat("v4"),
        };
    }

    public class ViaTransform
    {
        public Vector3 Position = Vector3.Zero;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Scale = Vector3.One;

        public static ViaTransform Parse(RszClass c) => new()
        {
            Position = ReadVec3(c, "v0"),
            Rotation = ReadQuat(c, "v1"),
            Scale    = ReadVec3(c, "v2"),
        };

        private static Vector3 ReadVec3(RszClass c, string name)
        {
            if (c.TryGet<RszVec3>(name, out var v)) return new Vector3(v.X, v.Y, v.Z);
            //Raw-bytes fallback: vec3 = 16 bytes (3 floats + 4 bytes padding) in RE Engine.
            if (c.TryGet<byte[]>(name, out var raw) && raw?.Length >= 12)
                return new Vector3(BitConverter.ToSingle(raw, 0), BitConverter.ToSingle(raw, 4), BitConverter.ToSingle(raw, 8));
            return Vector3.Zero;
        }

        private static Quaternion ReadQuat(RszClass c, string name)
        {
            if (c.TryGet<RszQuaternion>(name, out var q)) return new Quaternion(q.X, q.Y, q.Z, q.W);
            if (c.TryGet<byte[]>(name, out var raw) && raw?.Length >= 16)
                return new Quaternion(BitConverter.ToSingle(raw, 0), BitConverter.ToSingle(raw, 4),
                                      BitConverter.ToSingle(raw, 8), BitConverter.ToSingle(raw, 12));
            return Quaternion.Identity;
        }
    }
}
