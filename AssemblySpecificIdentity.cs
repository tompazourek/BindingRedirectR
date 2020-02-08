using System;
using System.Reflection;

namespace BindingRedirectR
{
    internal readonly struct AssemblySpecificIdentity : IEquatable<AssemblySpecificIdentity>
    {
        public AssemblyGroupIdentity Group { get; }
        public Version Version { get; }

        public AssemblySpecificIdentity(AssemblyName assemblyName)
        {
            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));

            if (string.IsNullOrEmpty(assemblyName.Name))
                throw new ArgumentException("Assembly's name cannot be empty.", nameof(assemblyName));

            Group = new AssemblyGroupIdentity(assemblyName);
            Version = assemblyName.Version;
        }

        private string AssemblyString
            => $"{Group.Name}, Version={Version} Culture={Group.Culture}, PublicKeyToken={Group.PublicKeyToken ?? "null"}";

        public override string ToString() => AssemblyString;

        #region Equality

        public bool Equals(AssemblySpecificIdentity other) => Group.Equals(other.Group) && Equals(Version, other.Version);

        public override bool Equals(object obj) => obj is AssemblySpecificIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Group.GetHashCode() * 397) ^ (Version != null ? Version.GetHashCode() : 0);
            }
        }

        public static bool operator ==(AssemblySpecificIdentity left, AssemblySpecificIdentity right) => left.Equals(right);

        public static bool operator !=(AssemblySpecificIdentity left, AssemblySpecificIdentity right) => !left.Equals(right);

        #endregion
    }
}