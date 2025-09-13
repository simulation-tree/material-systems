using System;
using Unmanaged;
using Worlds;

namespace Materials.Systems
{
    public readonly struct ShaderKey : IEquatable<ShaderKey>
    {
        public readonly World world;
        public readonly long addressHash;

        public ShaderKey(World world, ASCIIText256 address)
        {
            this.addressHash = address.GetLongHashCode();
            this.world = world;
        }

        public readonly override bool Equals(object? obj)
        {
            return obj is ShaderKey key && Equals(key);
        }

        public readonly bool Equals(ShaderKey other)
        {
            return addressHash.Equals(other.addressHash) && world.Equals(other.world);
        }

        public readonly override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + (int)addressHash;
            hash = hash * 31 + world.GetHashCode();
            return hash;
        }

        public static bool operator ==(ShaderKey left, ShaderKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ShaderKey left, ShaderKey right)
        {
            return !(left == right);
        }
    }
}