using System;
using Unmanaged;
using Worlds;

namespace Materials.Systems
{
    public readonly struct ShaderKey : IEquatable<ShaderKey>
    {
        public readonly World world;
        public readonly FixedString address;

        public ShaderKey(World world, FixedString address)
        {
            this.address = address;
            this.world = world;
        }

        public readonly override bool Equals(object? obj)
        {
            return obj is ShaderKey key && Equals(key);
        }

        public readonly bool Equals(ShaderKey other)
        {
            return address.Equals(other.address) && world.Equals(other.world);
        }

        public readonly override int GetHashCode()
        {
            return HashCode.Combine(address, world);
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