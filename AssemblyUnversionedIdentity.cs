using System;
using System.Linq;
using System.Reflection;

namespace BindingRedirectR
{
    internal readonly struct AssemblyUnversionedIdentity : IEquatable<AssemblyUnversionedIdentity>, IComparable<AssemblyUnversionedIdentity>, IComparable
    {
        public string Name { get; }
        public string PublicKeyToken { get; }
        public string Culture { get; }

        public AssemblyUnversionedIdentity(AssemblyName assemblyName)
        {
            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));

            if (string.IsNullOrEmpty(assemblyName.Name))
                throw new ArgumentException("Assembly's name cannot be empty.", nameof(assemblyName));

            Name = assemblyName.Name;

            var publicKeyTokenBytes = assemblyName.GetPublicKeyToken();
            if (publicKeyTokenBytes?.Any() == true)
            {
                PublicKeyToken = string.Concat(publicKeyTokenBytes.Select(x => x.ToString("x2")));
            }
            else
            {
                PublicKeyToken = null;
            }

            if (!string.IsNullOrEmpty(assemblyName.CultureName))
            {
                Culture = assemblyName.CultureName;
            }
            else
            {
                Culture = "neutral";
            }
        }

        public override string ToString()
            => $"{Name}, Culture={Culture}, PublicKeyToken={PublicKeyToken ?? "null"}";

        #region Equality

        public bool Equals(AssemblyUnversionedIdentity other)
            => Name == other.Name && PublicKeyToken == other.PublicKeyToken && Culture == other.Culture;

        public override bool Equals(object obj)
            => obj is AssemblyUnversionedIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PublicKeyToken != null ? PublicKeyToken.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Culture != null ? Culture.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(AssemblyUnversionedIdentity left, AssemblyUnversionedIdentity right) => left.Equals(right);

        public static bool operator !=(AssemblyUnversionedIdentity left, AssemblyUnversionedIdentity right) => !left.Equals(right);

        #endregion

        #region Comparison

        public int CompareTo(AssemblyUnversionedIdentity other)
        {
            var nameComparison = string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
            if (nameComparison != 0) return nameComparison;
            var cultureComparison = string.Compare(Culture, other.Culture, StringComparison.OrdinalIgnoreCase);
            if (cultureComparison != 0) return cultureComparison;
            return string.Compare(PublicKeyToken, other.PublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        public int CompareTo(object obj)
        {
            if (ReferenceEquals(null, obj)) return 1;
            return obj is AssemblyUnversionedIdentity other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(AssemblyUnversionedIdentity)}");
        }

        public static bool operator <(AssemblyUnversionedIdentity left, AssemblyUnversionedIdentity right) => left.CompareTo(right) < 0;

        public static bool operator >(AssemblyUnversionedIdentity left, AssemblyUnversionedIdentity right) => left.CompareTo(right) > 0;

        public static bool operator <=(AssemblyUnversionedIdentity left, AssemblyUnversionedIdentity right) => left.CompareTo(right) <= 0;

        public static bool operator >=(AssemblyUnversionedIdentity left, AssemblyUnversionedIdentity right) => left.CompareTo(right) >= 0;

        #endregion
    }
}