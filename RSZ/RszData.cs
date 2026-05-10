using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeX.Games.RE7.RSZ
{
    public class RSZData
    {
        public uint Version;
        public int ObjectCount;
        public int InstanceCount;
        public int UserDataCount;
        public List<int> ObjectTable = new();
        public List<RszInstanceInfo> Instances = new();
        public List<RszUserDataInfo> UserData = new();
        public List<RszClass> Classes;
    }

    public struct RszInstanceInfo
    {
        public uint TypeId;
        public uint CRC;
        public string Name;
    }

    public struct RszUserDataInfo
    {
        public uint InstanceId;
        public uint TypeId;
        public string Name;
    }

    public struct RszClassField
    {
        public string Name;
        public string Type;
        public object Value;
    }

    public class RszClass
    {
        public string Name;
        public List<RszClassField> Fields = new();

        private Dictionary<string, object> _fieldMap;
        private Dictionary<string, object> FieldMap =>
            _fieldMap ??= Fields.ToDictionary(f => f.Name, f => f.Value);

        public bool TryGet<T>(string name, out T value)
        {
            if (FieldMap.TryGetValue(name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public T Get<T>(string name) => TryGet<T>(name, out var v) ? v : default;

        public bool GetBool(string name)
        {
            if (TryGet<bool>(name, out var b)) return b;
            if (TryGet<byte>(name, out var by)) return by != 0;
            if (TryGet<byte[]>(name, out var arr) && arr?.Length > 0) return arr[0] != 0;
            return false;
        }

        public float GetFloat(string name)
        {
            if (TryGet<float>(name, out var f)) return f;
            if (TryGet<byte[]>(name, out var arr) && arr?.Length >= 4) return BitConverter.ToSingle(arr, 0);
            return 0f;
        }
    }
}
