namespace VBNetTweaks.Utils
{
    public static class RpcSerializer
    {
        private const byte TYPE_NONE      = 0;
        private const byte TYPE_INT       = 1;
        private const byte TYPE_FLOAT     = 2;
        private const byte TYPE_STRING    = 3;
        private const byte TYPE_BOOL      = 4;
        private const byte TYPE_VECTOR3   = 5;
        private const byte TYPE_QUAT      = 6;
        private const byte TYPE_LONG      = 7;
        private const byte TYPE_DOUBLE    = 8;
        private const byte TYPE_VECTOR2I  = 9;
        private const byte TYPE_VECTOR2S  = 10;
        private const byte TYPE_BYTES     = 11;
        private const byte TYPE_ZDOID     = 12;
        private const byte TYPE_ZPACKAGE  = 13;
        private const byte TYPE_ERROR     = 255;

        private delegate void WriteDelegate(ZPackage pkg, object value);
        private delegate object ReadDelegate(ZPackage pkg);

        private static readonly Dictionary<Type, WriteDelegate> _writers = new()
        {
            [typeof(int)] = (pkg, val) => { pkg.Write(TYPE_INT); pkg.Write((int)val); },
            [typeof(float)] = (pkg, val) => { pkg.Write(TYPE_FLOAT); pkg.Write((float)val); },
            [typeof(string)] = (pkg, val) => { pkg.Write(TYPE_STRING); pkg.Write((string)val); },
            [typeof(bool)] = (pkg, val) => { pkg.Write(TYPE_BOOL); pkg.Write((bool)val); },
            [typeof(Vector3)] = (pkg, val) => { pkg.Write(TYPE_VECTOR3); pkg.Write((Vector3)val); },
            [typeof(Quaternion)] = (pkg, val) => { pkg.Write(TYPE_QUAT); pkg.Write((Quaternion)val); },
            [typeof(long)] = (pkg, val) => { pkg.Write(TYPE_LONG); pkg.Write((long)val); },
            [typeof(double)] = (pkg, val) => { pkg.Write(TYPE_DOUBLE); pkg.Write((double)val); },
            [typeof(Vector2i)] = (pkg, val) => { pkg.Write(TYPE_VECTOR2I); pkg.Write((Vector2i)val); },
            [typeof(Vector2s)] = (pkg, val) => { pkg.Write(TYPE_VECTOR2S); pkg.Write((Vector2s)val); },
            [typeof(byte[])] = (pkg, val) => { pkg.Write(TYPE_BYTES); pkg.Write((byte[])val); },
            [typeof(ZDOID)] = (pkg, val) => { pkg.Write(TYPE_ZDOID); pkg.Write((ZDOID)val); },
            [typeof(ZPackage)] = (pkg, val) => 
            { 
                pkg.Write(TYPE_ZPACKAGE); 
                byte[] data = ((ZPackage)val).GetArray();
                pkg.Write(data.Length);
                pkg.Write(data);
            }
        };

        private static readonly Dictionary<byte, ReadDelegate> _readers = new()
        {
            [TYPE_NONE] = (pkg) => null,
            [TYPE_INT] = (pkg) => pkg.ReadInt(),
            [TYPE_FLOAT] = (pkg) => pkg.ReadSingle(),
            [TYPE_STRING] = (pkg) => pkg.ReadString(),
            [TYPE_BOOL] = (pkg) => pkg.ReadBool(),
            [TYPE_VECTOR3] = (pkg) => pkg.ReadVector3(),
            [TYPE_QUAT] = (pkg) => pkg.ReadQuaternion(),
            [TYPE_LONG] = (pkg) => pkg.ReadLong(),
            [TYPE_DOUBLE] = (pkg) => pkg.ReadDouble(),
            [TYPE_VECTOR2I] = (pkg) => pkg.ReadVector2i(),
            [TYPE_VECTOR2S] = (pkg) => pkg.ReadVector2s(),
            [TYPE_BYTES] = (pkg) => pkg.ReadByteArray(),
            [TYPE_ZDOID] = (pkg) => pkg.ReadZDOID(),
            [TYPE_ZPACKAGE] = (pkg) => 
            {
                int len = pkg.ReadInt();
                byte[] data = pkg.ReadByteArray(len);
                return new ZPackage(data);
            }
        };

        public static void WriteArg(ZPackage pkg, object arg)
        {
            if (arg == null)
            {
                pkg.Write(TYPE_NONE);
                return;
            }

            Type type = arg.GetType();
            
            switch (type)
            {
                case Type t when t == typeof(int):
                    pkg.Write(TYPE_INT);
                    pkg.Write((int)arg);
                    return;
                    
                case Type t when t == typeof(float):
                    pkg.Write(TYPE_FLOAT);
                    pkg.Write((float)arg);
                    return;
                    
                case Type t when t == typeof(string):
                    pkg.Write(TYPE_STRING);
                    pkg.Write((string)arg);
                    return;
                    
                case Type t when t == typeof(bool):
                    pkg.Write(TYPE_BOOL);
                    pkg.Write((bool)arg);
                    return;
                    
                case Type t when t == typeof(Vector3):
                    pkg.Write(TYPE_VECTOR3);
                    pkg.Write((Vector3)arg);
                    return;
                    
                case Type t when t == typeof(ZDOID):
                    pkg.Write(TYPE_ZDOID);
                    pkg.Write((ZDOID)arg);
                    return;
                    
                case Type t when t == typeof(long):
                    pkg.Write(TYPE_LONG);
                    pkg.Write((long)arg);
                    return;
            }

            if (_writers.TryGetValue(type, out var writer))
            {
                writer(pkg, arg);
            }
            else
            {
                Helper.LogDebug($"RpcSerializer: unsupported arg type {type}");
                pkg.Write(TYPE_ERROR);
                pkg.Write(type.FullName ?? "unknown");
            }
        }

        public static void WriteArgs(ZPackage pkg, object[] args)
        {
            pkg.WriteNumItems(args.Length);
            
            for (int i = 0; i < args.Length; i++)
            {
                WriteArg(pkg, args[i]);
            }
        }

        public static object ReadArg(ZPackage pkg)
        {
            byte type = pkg.ReadByte();
            
            switch (type)
            {
                case TYPE_NONE:      return null;
                case TYPE_INT:       return pkg.ReadInt();
                case TYPE_FLOAT:     return pkg.ReadSingle();
                case TYPE_STRING:    return pkg.ReadString();
                case TYPE_BOOL:      return pkg.ReadBool();
                case TYPE_VECTOR3:   return pkg.ReadVector3();
                case TYPE_QUAT:      return pkg.ReadQuaternion();
                case TYPE_LONG:      return pkg.ReadLong();
                case TYPE_DOUBLE:    return pkg.ReadDouble();
                case TYPE_VECTOR2I:  return pkg.ReadVector2i();
                case TYPE_VECTOR2S:  return pkg.ReadVector2s();
                case TYPE_BYTES:     return pkg.ReadByteArray();
                case TYPE_ZDOID:     return pkg.ReadZDOID();
            }

            if (_readers.TryGetValue(type, out var reader))
            {
                return reader(pkg);
            }

            if (type == TYPE_ERROR)
            {
                string typeName = pkg.ReadString();
                Helper.LogDebug($"RpcSerializer: received unsupported type '{typeName}'");
                return null;
            }

            Helper.LogDebug($"RpcSerializer: unknown type id {type}");
            return null;
        }

        public static object[] ReadArgs(ZPackage pkg)
        {
            int count = pkg.ReadNumItems();
            object[] args = new object[count];
            
            for (int i = 0; i < count; i++)
            {
                args[i] = ReadArg(pkg);
            }
            return args;
        }

        public static void ReadArgsInto(ZPackage pkg, object[] args, int startIndex, int count)
        {
            for (int i = 0; i < count; i++)
            {
                args[startIndex + i] = ReadArg(pkg);
            }
        }
    }
}