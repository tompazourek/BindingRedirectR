using System.Collections.Generic;

namespace BindingRedirectR
{
    public class InputParameters
    {
        /// <summary>
        /// Main assembly for which to consider the dependency tree.
        /// Either a file path or a fully qualified assembly name.
        /// </summary>
        public string MainAssembly { get; set; }

        /// <summary>
        /// Base directory from which the relative paths will be executed.
        /// </summary>
        public string BaseDirectory { get; set; }

        /// <summary>
        /// All assemblies to load.
        /// Either a file path, file path pattern, or a fully qualified assembly name.
        /// </summary>
        public IList<string> Assemblies { get; set; } = new List<string>();

        /// <summary>
        /// Additional dependencies that aren't detected in the assemblies manifest, but we want to consider.
        /// </summary>
        public IList<AdditionalDependency> AdditionalDependencies { get; } = new List<AdditionalDependency>();
        
        public class AdditionalDependency
        {
            /// <summary>
            /// Assembly that depends on the listed dependencies.
            /// Either a file path or a fully qualified assembly name.
            /// </summary>
            public string Dependant { get; set; }

            /// <summary>
            /// Dependencies.
            /// Either a file path, file path pattern, or a fully qualified assembly name.
            /// </summary>
            public IList<string> Dependencies { get; set; } = new List<string>();
        }
    }
}