using System;
using System.Collections.Generic;

namespace BindingRedirectR
{
    internal readonly struct AssemblyNode
    {
        public AssemblyGroupIdentity AssemblyGroupIdentity { get; }

        public ISet<AssemblyPathVersion> PathVersions { get; }

        public ISet<AssemblyEdge> Dependencies { get; }

        public ISet<AssemblyEdge> Dependants { get; }

        public AssemblyNode(in AssemblyGroupIdentity assemblyGroupIdentity)
        {
            AssemblyGroupIdentity = assemblyGroupIdentity;
            PathVersions = new HashSet<AssemblyPathVersion>();
            Dependencies = new HashSet<AssemblyEdge>();
            Dependants = new HashSet<AssemblyEdge>();
        }

        public override string ToString() => AssemblyGroupIdentity.ToString();
    }
}