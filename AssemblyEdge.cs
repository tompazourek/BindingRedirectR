using System;

namespace BindingRedirectR
{
    internal readonly struct AssemblyEdge : IEquatable<AssemblyEdge>
    {
        public AssemblyNode TargetNode { get; }
        public Version TargetVersion { get; }
        public Version SourceVersion { get; }

        public AssemblyEdge(AssemblyNode targetNode, Version targetVersion, Version sourceVersion)
        {
            TargetNode = targetNode;
            TargetVersion = targetVersion;
            SourceVersion = sourceVersion;
        }

        /// <summary>
        /// Assembly string in the format useful for loading assemblies.
        /// </summary>
        public string GetTargetAsAssemblyString() => $"{TargetNode.AssemblyIdentity.Name}, Version={TargetVersion}, Culture={TargetNode.AssemblyIdentity.Culture}, PublicKeyToken={TargetNode.AssemblyIdentity.PublicKeyToken ?? "null"}";

        public override string ToString() => $"[{SourceVersion}] {GetTargetAsAssemblyString()}";

        #region Equality

        public bool Equals(AssemblyEdge other) => TargetNode.Equals(other.TargetNode) && Equals(TargetVersion, other.TargetVersion) && Equals(SourceVersion, other.SourceVersion);

        public override bool Equals(object obj) => obj is AssemblyEdge other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TargetNode.GetHashCode();
                hashCode = (hashCode * 397) ^ (TargetVersion != null ? TargetVersion.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (SourceVersion != null ? SourceVersion.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(AssemblyEdge left, AssemblyEdge right) => left.Equals(right);

        public static bool operator !=(AssemblyEdge left, AssemblyEdge right) => !left.Equals(right);

        #endregion
    }
}