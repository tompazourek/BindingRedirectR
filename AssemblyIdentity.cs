﻿using System;
using System.Linq;
using System.Reflection;

namespace BindingRedirectR
{
    internal readonly struct AssemblyIdentity : IEquatable<AssemblyIdentity>
    {
        public string Name { get; }
        public string PublicKeyToken { get; }
        public string Culture { get; }

        public AssemblyIdentity(AssemblyName assemblyName)
        {
            if (assemblyName == null)
                throw new ArgumentNullException(nameof(assemblyName));

            if (string.IsNullOrEmpty(assemblyName.Name))
                throw new ArgumentException("Assembly's name cannot be empty.", nameof(assemblyName));

            Name = assemblyName.Name;

            var publicKeyTokenBytes = assemblyName.GetPublicKeyToken();
            if (publicKeyTokenBytes.Any())
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

        public bool Equals(AssemblyIdentity other)
            => Name == other.Name && PublicKeyToken == other.PublicKeyToken && Culture == other.Culture;

        public override bool Equals(object obj)
            => obj is AssemblyIdentity other && Equals(other);

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

        public static bool operator ==(AssemblyIdentity left, AssemblyIdentity right) => left.Equals(right);

        public static bool operator !=(AssemblyIdentity left, AssemblyIdentity right) => !left.Equals(right);

        #endregion
    }
}