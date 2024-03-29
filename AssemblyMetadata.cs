﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BindingRedirectR
{
    [Serializable]
    internal class AssemblyMetadata
    {
        private readonly string[] _referenceAssemblyNames;
        public string AssemblyName { get; }
        public string Location { get; }

        public IReadOnlyList<string> ReferenceAssemblyNames => _referenceAssemblyNames;

        public AssemblyMetadata(Assembly assembly)
            : this(assembly.FullName, assembly.Location, assembly.GetReferencedAssemblies().Select(x => x.FullName).ToArray())
        {
        }

        public AssemblyMetadata(string assemblyName, string location, string[] referenceAssemblyNames)
        {
            AssemblyName = assemblyName;
            Location = location;
            _referenceAssemblyNames = referenceAssemblyNames;
        }

        public override string ToString() => AssemblyName;
    }
}