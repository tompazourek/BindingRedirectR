using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Serilog;
using static System.Console;

namespace BindingRedirectR
{
    /// <remarks>
    /// TODO: can we add some way of adding other missed references (dynamic, aka runtime dependencies -- stuff referenced from Web.config, dynamically loaded assemblies, etc.)
    /// TODO: allow to specify the main assembly
    /// TODO: aggregate the output as the list of binding redirects, provide reasoning behind each, e.g.:
    /// - redirecting to 12.0.0.0, because AssemblyFoo references 11.0.0.0 and AssemblyBar and AssemblyBaz references 10.0.0.0
    /// </remarks>
    internal class Program
    {
        private static readonly ILogger Log = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        private static readonly string Separator = new string('-', 20);
        private static readonly ConcurrentDictionary<AssemblyIdentity, AssemblyNode> NodesByIdentity = new ConcurrentDictionary<AssemblyIdentity, AssemblyNode>();
        private static readonly Version VersionZero = new Version(0, 0, 0, 0);

        private static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Error(e.ExceptionObject as Exception, "Unhandled exception occurred.");
                Log.Information("Press any key to exit.");
                ReadKey();
                Environment.Exit(1);
            };

            var assemblyPaths = SampleInput.SomePaths;
            Run(assemblyPaths);

            Log.Information(Separator);
        }

        private static void Run(IEnumerable<string> assemblyPaths)
        {
            Log.Verbose("Run started.");

            var dependenciesToCheckForGac = new Stack<AssemblyEdge>();

            // load assemblies from files
            foreach (var path in assemblyPaths)
            {
                Log.Debug(Separator);
                Log.Debug("Loading file {Path}.", path);

                Assembly assembly;
                try
                {
                    assembly = Assembly.ReflectionOnlyLoadFrom(path);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Couldn't load assembly from [{Path}].", path);
                    continue;
                }

                var assemblyName = assembly.GetName();
                var assemblyIdentity = new AssemblyIdentity(assemblyName);
                var assemblyVersion = assemblyName.Version;
                var assemblyNode = NodesByIdentity.GetOrAdd(assemblyIdentity, x => new AssemblyNode(x));
                assemblyNode.PathVersions.Add(new AssemblyPathVersion(path, assemblyVersion));

                Log.Debug("Loaded [{AssemblyIdentity}] Version={Version}.", assemblyIdentity, assemblyVersion);

                foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
                {
                    var referencedAssemblyIdentity = new AssemblyIdentity(referencedAssemblyName);
                    var referencedAssemblyVersion = referencedAssemblyName.Version;
                    var referencedAssemblyNode = NodesByIdentity.GetOrAdd(referencedAssemblyIdentity, x => new AssemblyNode(x));

                    var dependency = new AssemblyEdge(referencedAssemblyNode, referencedAssemblyVersion, assemblyVersion);
                    var dependant = new AssemblyEdge(assemblyNode, assemblyVersion, referencedAssemblyVersion);
                    assemblyNode.Dependencies.Add(dependency);
                    referencedAssemblyNode.Dependants.Add(dependant);
                    dependenciesToCheckForGac.Push(dependency);

                    Log.Debug("Depends on [{AssemblyIdentity}] Version={Version}.", referencedAssemblyIdentity, referencedAssemblyVersion);
                }
            }

            // load assemblies from GAC
            var assemblyStringsUnloadable = new HashSet<string>();
            while (dependenciesToCheckForGac.Any())
            {
                var edge = dependenciesToCheckForGac.Pop();
                if (edge.TargetNode.PathVersions.Any(x => x.Version == edge.TargetVersion))
                {
                    // it's already loaded
                    continue;
                }

                var assemblyString = edge.GetTargetAsAssemblyString();

                if (assemblyStringsUnloadable.Contains(assemblyString))
                {
                    // errored before, won't try again
                    continue;
                }

                Log.Information(Separator);
                Log.Information("Loading from GAC [{AssemblyString}].", assemblyString);

                Assembly assembly;
                try
                {
                    assembly = Assembly.ReflectionOnlyLoad(assemblyString);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Couldn't load GAC assembly [{AssemblyString}].", assemblyString);
                    assemblyStringsUnloadable.Add(assemblyString);
                    continue;
                }

                var path = assembly.Location;
                var assemblyName = assembly.GetName();
                var assemblyIdentity = new AssemblyIdentity(assemblyName);
                var assemblyVersion = assemblyName.Version;
                var assemblyNode = NodesByIdentity.GetOrAdd(assemblyIdentity, x => new AssemblyNode(x));
                assemblyNode.PathVersions.Add(new AssemblyPathVersion(path, assemblyVersion));

                Log.Information("Loaded [{AssemblyIdentity}] Version={Version}.", assemblyIdentity, assemblyVersion);

                foreach (var referencedAssemblyName in assembly.GetReferencedAssemblies())
                {
                    var referencedAssemblyIdentity = new AssemblyIdentity(referencedAssemblyName);
                    var referencedAssemblyVersion = referencedAssemblyName.Version;
                    var referencedAssemblyNode = NodesByIdentity.GetOrAdd(referencedAssemblyIdentity, x => new AssemblyNode(x));

                    var dependency = new AssemblyEdge(referencedAssemblyNode, referencedAssemblyVersion, assemblyVersion);
                    var dependant = new AssemblyEdge(assemblyNode, assemblyVersion, referencedAssemblyVersion);
                    assemblyNode.Dependencies.Add(dependency);
                    referencedAssemblyNode.Dependants.Add(dependant);
                    dependenciesToCheckForGac.Push(dependency);

                    Log.Information("Depends on [{AssemblyIdentity}] Version={Version}.", referencedAssemblyIdentity, referencedAssemblyVersion);
                }
            }

            var nodes = NodesByIdentity.Values
                .OrderBy(x => x.AssemblyIdentity.Name)
                .ThenBy(x => x.AssemblyIdentity.Culture)
                .ThenBy(x => x.AssemblyIdentity.PublicKeyToken)
                .ToList();

            Log.Information(Separator);
            Log.Information("Collected {NodeCount} nodes:", nodes.Count);

            foreach (var node in nodes)
            {
                Log.Information("{Node}", node);
            }

            Log.Information(Separator);

            var leafNodes = nodes.Where(x => !x.Dependants.Any()).ToList();
            Log.Information("These are the {NodeCount} leaf nodes (no dependants):", leafNodes.Count);

            var rootNodes = nodes.Where(x => !x.Dependencies.Any()).ToList();
            Log.Information("These are the {NodeCount} root nodes (no dependencies):", rootNodes.Count);

            foreach (var node in rootNodes)
            {
                Log.Information("{Node}", node);
            }

            foreach (var node in rootNodes)
            {
                node.VisitNodeAndDependants(ProcessNode);
            }

            Log.Verbose("Run finished.");
        }

        private static void ProcessNode(AssemblyNode node)
        {
            Log.Information(Separator);
            Log.Information(Separator);

            Log.Information("Processing dependency [{AssemblyIdentity}].", node.AssemblyIdentity);

            var directDependants = node.Dependants;
            var dependants = node.GetDependantsIncludingTransient()
                .OrderBy(x => x.TargetNode.AssemblyIdentity.Name)
                .ThenBy(x => x.TargetNode.AssemblyIdentity.Culture)
                .ThenBy(x => x.TargetNode.AssemblyIdentity.PublicKeyToken)
                .ThenBy(x => x.TargetVersion)
                .ToList();

            var dependantsByVersion = dependants
                .GroupBy(x => x.SourceVersion)
                .OrderBy(x => x.Key)
                .ToList();

            foreach (var versionGroup in dependantsByVersion)
            {
                Log.Information(Separator);

                Log.Information("[{Version}] Assemblies with direct dependencies:", versionGroup.Key);
                var versionGroupDirect = versionGroup.Where(x => directDependants.Contains(x)).ToList();
                foreach (var assemblyIdentity in versionGroupDirect.Select(x => x.TargetNode.AssemblyIdentity).ToHashSet())
                {
                    Log.Information("{AssemblyIdentity}", assemblyIdentity);
                }

                Log.Information("[{Version}] Assemblies with transient dependencies:", versionGroup.Key);
                var versionGroupTransient = versionGroup.Where(x => !directDependants.Contains(x)).ToList();
                foreach (var assemblyIdentity in versionGroupTransient.Select(x => x.TargetNode.AssemblyIdentity).ToHashSet())
                {
                    Log.Information("{AssemblyIdentity}", assemblyIdentity);
                }
            }

            Log.Information(Separator);

            Log.Information("Results for dependency [{AssemblyIdentity}].", node.AssemblyIdentity);

            // find highest referenced and other referenced
            var referencedVersions = dependantsByVersion.Select(x => x.Key).ToHashSet();
            Version highestReferencedVersion = null;
            if (!referencedVersions.Any())
            {
                Log.Information("There are no assemblies that would reference this one.");
            }
            else
            {
                highestReferencedVersion = referencedVersions.Max();
                Log.Information("Highest referenced version is: {Version}", highestReferencedVersion);

                var otherReferencedVersions = referencedVersions
                    .Where(x => x != highestReferencedVersion)
                    .OrderBy(x => x)
                    .ToList();

                if (!otherReferencedVersions.Any())
                {
                    Log.Information("There are no other referenced versions.");
                }
                else
                {
                    Log.Information("Then there are also these other referenced versions:");
                    foreach (var version in otherReferencedVersions)
                    {
                        Log.Information("{Version}", version);
                    }
                }
            }

            // find highest loaded and other loaded
            var loadedVersions = node.PathVersions;
            Version highestLoadedVersion = null;
            if (!loadedVersions.Any())
            {
                Log.Warning("We couldn't locate this assembly at all in the input paths or the GAC.");
            }
            else
            {
                var highestLoadedPathVersion = loadedVersions.OrderByDescending(x => x.Version).First();
                highestLoadedVersion = highestLoadedPathVersion.Version;
                Log.Information("Highest loaded version is: {Version} [{Path}]", highestLoadedPathVersion.Version, highestLoadedPathVersion.Path);

                var otherLoadedPathVersions = loadedVersions
                    .Where(x => x.Version != highestLoadedPathVersion.Version)
                    .OrderBy(x => x.Version)
                    .ToList();
                
                if (!otherLoadedPathVersions.Any())
                {
                    Log.Information("There are no other loaded versions.");
                }
                else
                {
                    Log.Information("Then there are also these other loaded versions:");
                    foreach (var pathVersion in otherLoadedPathVersions)
                    {
                        Log.Information("{Version} [{Path}]", pathVersion.Version, pathVersion.Path);
                    }
                }
            }

            // recommend binding redirect
           var targetVersion = (highestLoadedVersion ?? VersionZero) > (highestReferencedVersion ?? VersionZero)
                ? highestLoadedVersion
                : highestReferencedVersion;

            targetVersion = targetVersion ?? VersionZero;

            if (targetVersion == VersionZero)
            {
                Log.Information("No useful binding redirect can be made, the version is always {Version}", targetVersion);
            }
            else
            {
                var redirectApplyTargets = dependants.Where(x => x.SourceVersion != targetVersion)
                    .Select(x => x.TargetNode.AssemblyIdentity)
                    .Distinct()
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Culture)
                    .ThenBy(x => x.PublicKeyToken)
                    .ToList();

                if (redirectApplyTargets.Any())
                {
                    Log.Information("Recommended binding redirect: oldVersion=\"{VersionFrom}-{VersionTo}\" newVersion=\"{NewVersion}\"", VersionZero, targetVersion, targetVersion);
                    Log.Information("Recommended to apply to:");

                    foreach (var redirectApplyTarget in redirectApplyTargets)
                    {
                        Log.Information("{AssemblyIdentity}", redirectApplyTarget);
                    }
                }
                else
                {
                    Log.Information("Likely redundant binding redirect: oldVersion=\"{VersionFrom}-{VersionTo}\" newVersion=\"{NewVersion}\"", VersionZero, targetVersion, targetVersion);
                }
            }

            if (highestLoadedVersion != null && highestReferencedVersion != null && highestLoadedVersion != highestReferencedVersion)
            {
                Log.Warning("There is a mismatch between the highest referenced and highest loaded version.");

                if (highestLoadedVersion != targetVersion)
                {
                    var alternativeRedirectApplyTargets = dependants.Where(x => x.SourceVersion != highestLoadedVersion)
                        .Select(x => x.TargetNode.AssemblyIdentity)
                        .Distinct()
                        .OrderBy(x => x.Name)
                        .ThenBy(x => x.Culture)
                        .ThenBy(x => x.PublicKeyToken)
                        .ToList();

                    if (alternativeRedirectApplyTargets.Any())
                    {
                        Log.Information("Alternative binding redirect: oldVersion=\"{VersionFrom}-{VersionTo}\" newVersion=\"{NewVersion}\"", VersionZero, targetVersion, highestLoadedVersion);
                        Log.Information("Alternative to apply to:");

                        foreach (var redirectApplyTarget in alternativeRedirectApplyTargets)
                        {
                            Log.Information("{AssemblyIdentity}", redirectApplyTarget);
                        }
                    }
                    else
                    {
                        Log.Information("Likely redundant alternative binding redirect: oldVersion=\"{VersionFrom}-{VersionTo}\" newVersion=\"{NewVersion}\"", VersionZero, targetVersion, highestLoadedVersion);
                    }
                }
            }
        }
    }
}