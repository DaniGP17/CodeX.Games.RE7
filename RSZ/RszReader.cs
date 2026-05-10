using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeX.Games.RE7.Common;

namespace CodeX.Games.RE7.RSZ
{
    internal static class RszReader
    {
        public static RSZData Read(BinaryReader br)
        {
            var rsz = new RSZData();
            int startPosition = (int)br.BaseStream.Position;

            br.ReadBytes(4); //"RSZ\0" magic
            rsz.Version = br.ReadUInt32();
            rsz.ObjectCount = br.ReadInt32();
            rsz.InstanceCount = br.ReadInt32();
            rsz.UserDataCount = (int)br.ReadInt64();

            ulong instanceOffset = br.ReadUInt64();
            ulong dataOffset = br.ReadUInt64();
            ulong userDataOffset = br.ReadUInt64();

            //Object table: int per object, indexes into the instance list. The flat
            //layout is what makes "GameObject + N components" work — the GO is at
            //index G, and components are at G+1..G+N in this table.
            for (int i = 0; i < rsz.ObjectCount; i++)
                rsz.ObjectTable.Add(br.ReadInt32());

            br.BaseStream.Seek((long)instanceOffset + startPosition, SeekOrigin.Begin);
            for (int i = 0; i < rsz.InstanceCount; i++)
            {
                var instance = new RszInstanceInfo
                {
                    TypeId = br.ReadUInt32(),
                    CRC = br.ReadUInt32(),
                };
                instance.Name = RszRegistry.Current.GetByHash(instance.TypeId)?.Name ?? "NULL";
                if (string.IsNullOrEmpty(instance.Name)) instance.Name = "NULL";
                rsz.Instances.Add(instance);
            }

            br.BaseStream.Seek((long)userDataOffset + startPosition, SeekOrigin.Begin);
            for (int i = 0; i < rsz.UserDataCount; i++)
            {
                rsz.UserData.Add(new RszUserDataInfo
                {
                    InstanceId = br.ReadUInt32(),
                    TypeId = br.ReadUInt32(),
                    Name = br.ReadNullTerminatedString(br.ReadInt64(), Encoding.Unicode),
                });
            }

            var userDataInstanceIds = new HashSet<uint>(rsz.UserData.Select(ud => ud.InstanceId));

            br.BaseStream.Seek((long)dataOffset + startPosition, SeekOrigin.Begin);
            rsz.Classes = new List<RszClass>(new RszClass[rsz.InstanceCount]);
            for (int i = 0; i < rsz.InstanceCount; i++)
            {
                if (rsz.Instances[i].Name == "NULL")
                {
                    continue;
                }

                try
                {
                    var isUserData = userDataInstanceIds.Contains((uint)i);
                    var fields = isUserData
                        ? new List<RszClassField>()
                        : (RszRegistry.Current.GetFieldsByHash(rsz.Instances[i].TypeId)
                            ?.Select(f => new RszClassField
                            {
                                Name = f.Name,
                                Type = f.Type,
                                Value = RszValueReader.ReadValue(br, f, rsz.Instances[i].Name)
                            }).ToList()
                           ?? new List<RszClassField>());

                    rsz.Classes[i] = new RszClass
                    {
                        Name = rsz.Instances[i].Name,
                        Fields = fields,
                    };
                }
                catch (Exception ex)
                {
                    //One bad type definition can desync the whole instance list.
                    Console.WriteLine($"RszReader: failed to read instance {i} (TypeId 0x{rsz.Instances[i].TypeId:X8} '{rsz.Instances[i].Name}'): {ex.Message}");
                    break;
                }
            }

            return rsz;
        }
    }
}
