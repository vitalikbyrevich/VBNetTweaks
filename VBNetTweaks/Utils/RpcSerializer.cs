namespace VBNetTweaks.Utils
{
    public static class RpcSerializer
    {
        private const byte Type_None      = 0;
        private const byte Type_Int      = 1;
        private const byte Type_Float    = 2;
        private const byte Type_String   = 3;
        private const byte Type_Bool     = 4;
        private const byte Type_Vector3  = 5;
        private const byte Type_Quat     = 6;
        private const byte Type_Long     = 7;

        public static void WriteArg(ZPackage pkg, object arg)
        {
            if (arg is int)
            {
                pkg.Write(Type_Int);
                pkg.Write((int)arg);
            }
            else if (arg is float)
            {
                pkg.Write(Type_Float);
                pkg.Write((float)arg);
            }
            else if (arg is string)
            {
                pkg.Write(Type_String);
                pkg.Write((string)arg);
            }
            else if (arg is bool)
            {
                pkg.Write(Type_Bool);
                pkg.Write((bool)arg);
            }
            else if (arg is Vector3)
            {
                pkg.Write(Type_Vector3);
                pkg.Write((Vector3)arg);
            }
            else if (arg is Quaternion)
            {
                pkg.Write(Type_Quat);
                pkg.Write((Quaternion)arg);
            }
            else if (arg is long)
            {
                pkg.Write(Type_Long);
                pkg.Write((long)arg);
            }
            else
            {
                pkg.Write(Type_None);
                if (VBNetTweaks.DebugEnabled.Value) VBNetTweaks.LogDebug("RpcSerializer: unsupported arg type " + arg.GetType());
            }
        }

        public static object ReadArg(ZPackage pkg)
        {
            byte type = pkg.ReadByte();

            switch (type)
            {
                case Type_Int:     return pkg.ReadInt();
                case Type_Float:   return pkg.ReadSingle();
                case Type_String:  return pkg.ReadString();
                case Type_Bool:    return pkg.ReadBool();
                case Type_Vector3: return pkg.ReadVector3();
                case Type_Quat:    return pkg.ReadQuaternion();
                case Type_Long:    return pkg.ReadLong();
                case Type_None:
                default:
                    return null;
            }
        }
    }
}
