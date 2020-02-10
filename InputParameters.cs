using System.Collections.Generic;

namespace BindingRedirectR
{
    public class InputParameters
    {
        /// <summary>
        /// Main assembly for which to consider the dependency tree.
        /// Either a file path, or a fully qualified assembly name.
        /// </summary>
        public string MainAssembly { get; set; }

        /// <summary>
        /// All assemblies to load.
        /// Either a file path, or a fully qualified assembly name.
        /// </summary>
        public IList<string> Assemblies { get; set; }

        /// <summary>
        /// Additional dependencies that aren't detected in the assemblies manifest, but we want to consider.
        /// </summary>
        public IList<ManualDependency> AdditionalDependencies { get; } = new List<ManualDependency>();
        
        public class ManualDependency
        {
            public string Dependant { get; set; }
            public string Dependency { get; set; }
        }
    }
}