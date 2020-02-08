using System;
using System.Collections.Generic;

namespace BindingRedirectR
{
    internal readonly struct AssemblyNode
    {
        public AssemblyIdentity AssemblyIdentity { get; }

        public ISet<AssemblyPathVersion> PathVersions { get; }

        public ISet<AssemblyEdge> Dependencies { get; }

        public ISet<AssemblyEdge> Dependants { get; }

        public AssemblyNode(in AssemblyIdentity assemblyIdentity)
        {
            AssemblyIdentity = assemblyIdentity;
            PathVersions = new HashSet<AssemblyPathVersion>();
            Dependencies = new HashSet<AssemblyEdge>();
            Dependants = new HashSet<AssemblyEdge>();
        }

        public override string ToString() => AssemblyIdentity.ToString();
    }
}