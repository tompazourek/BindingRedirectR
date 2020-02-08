using System;

namespace BindingRedirectR
{
    internal readonly struct AssemblyPathVersion : IEquatable<AssemblyPathVersion>
    {
        public string Path { get; }
        public Version Version { get; }

        public AssemblyPathVersion(string path, Version version)
        {
            Path = path;
            Version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public override string ToString() 
            => $"Version={Version}, Path={Path}";

        #region Equality

        public bool Equals(AssemblyPathVersion other) => Path == other.Path && Equals(Version, other.Version);

        public override bool Equals(object obj) => obj is AssemblyPathVersion other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Path != null ? Path.GetHashCode() : 0) * 397) ^ (Version != null ? Version.GetHashCode() : 0);
            }
        }

        public static bool operator ==(AssemblyPathVersion left, AssemblyPathVersion right) => left.Equals(right);

        public static bool operator !=(AssemblyPathVersion left, AssemblyPathVersion right) => !left.Equals(right);

        #endregion
    }
}