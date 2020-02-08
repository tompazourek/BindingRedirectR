using System;

namespace BindingRedirectR
{
    public class InputParameters
    {
        /// <summary>
        /// File path to the main assembly to analyze (entry assembly). Can also be an "assembly string".
        /// </summary>
        public string MainAssemblySource { get; }

        /// <summary>
        /// File paths to process. Can also be an "assembly string" to load the assembly from GAC.
        /// The <see cref="MainAssemblySource"/> can be included here or not, doesn't matter.
        /// </summary>
        public string[] AssemblySources { get; }

        /// <summary>
        /// Additional references that aren't in the assemblies, but we want to use.
        /// </summary>
        public (string dependantSource, string dependencySource)[] ManualReferences { get; }

        public InputParameters
        (
            string[] assemblySources,
            string mainAssemblySource,
            (string dependantSource, string dependencySource)[] manualReferences
        )
        {
            AssemblySources = assemblySources ?? throw new ArgumentNullException(nameof(assemblySources));
            MainAssemblySource = mainAssemblySource ?? throw new ArgumentNullException(nameof(mainAssemblySource));
            ManualReferences = manualReferences ?? throw new ArgumentNullException(nameof(manualReferences));
        }
    }
}