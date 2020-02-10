using System;
using System.Collections.Generic;
using System.Reflection;

namespace BindingRedirectR
{
    internal readonly struct AssemblyVersionedIdentity : IEquatable<AssemblyVersionedIdentity>, IComparable<AssemblyVersionedIdentity>, IComparable
    {
        public AssemblyUnversionedIdentity Unversioned { get; }
        public Version Version { get; }

        public AssemblyVersionedIdentity(AssemblyName assemblyName)
        {
            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));

            if (string.IsNullOrEmpty(assemblyName.Name))
                throw new ArgumentException("Assembly's name cannot be empty.", nameof(assemblyName));

            Unversioned = new AssemblyUnversionedIdentity(assemblyName);
            Version = assemblyName.Version;
        }

        private string AssemblyString
            => $"{Unversioned.Name}, Version={Version} Culture={Unversioned.Culture}, PublicKeyToken={Unversioned.PublicKeyToken ?? "null"}";

        public override string ToString() => AssemblyString;

        #region Equality

        public bool Equals(AssemblyVersionedIdentity other) => Unversioned.Equals(other.Unversioned) && Equals(Version, other.Version);

        public override bool Equals(object obj) => obj is AssemblyVersionedIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Unversioned.GetHashCode() * 397) ^ (Version != null ? Version.GetHashCode() : 0);
            }
        }

        public static bool operator ==(AssemblyVersionedIdentity left, AssemblyVersionedIdentity right) => left.Equals(right);

        public static bool operator !=(AssemblyVersionedIdentity left, AssemblyVersionedIdentity right) => !left.Equals(right);

        #endregion

        #region Comparison

        public int CompareTo(AssemblyVersionedIdentity other)
        {
            var unversionedComparison = Unversioned.CompareTo(other.Unversioned);
            if (unversionedComparison != 0) return unversionedComparison;
            return Comparer<Version>.Default.Compare(Version, other.Version);
        }

        public int CompareTo(object obj)
        {
            if (ReferenceEquals(null, obj)) return 1;
            return obj is AssemblyVersionedIdentity other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(AssemblyVersionedIdentity)}");
        }

        public static bool operator <(AssemblyVersionedIdentity left, AssemblyVersionedIdentity right) => left.CompareTo(right) < 0;

        public static bool operator >(AssemblyVersionedIdentity left, AssemblyVersionedIdentity right) => left.CompareTo(right) > 0;

        public static bool operator <=(AssemblyVersionedIdentity left, AssemblyVersionedIdentity right) => left.CompareTo(right) <= 0;

        public static bool operator >=(AssemblyVersionedIdentity left, AssemblyVersionedIdentity right) => left.CompareTo(right) >= 0;

        #endregion
    }
}