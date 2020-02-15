using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using Serilog;
using static System.Console;

namespace BindingRedirectR
{
    internal class Program
    {
        #region Init

        // ReSharper disable once InconsistentNaming
        private static ILogger Logger;

        private static void Main(string[] args)
        {
            // parse input parameter
            var inputJsonPath = args?.Length > 0 ? args[0] : null;
            inputJsonPath = string.IsNullOrWhiteSpace(inputJsonPath) ? null : inputJsonPath;
            inputJsonPath = inputJsonPath ?? "sample-input.json";
            inputJsonPath = Path.GetFullPath(inputJsonPath);

            // clear log file initially
            var outputLogPath = $"{inputJsonPath}.log";
            File.Delete(outputLogPath);

            // set up logging
            Logger = (Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.Console()
                    .WriteTo.File(outputLogPath)
                    .CreateLogger())
                .ForContext<Program>();

            // set up unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Logger.Error(e.ExceptionObject as Exception, "Unhandled exception occurred.");
                Log.CloseAndFlush();
                Environment.Exit(1);
            };

            // parse inputs
            Logger.Information("Input file: {File}", inputJsonPath);
            Logger.Information("Output log: {File}", outputLogPath);
            Logger.Information(Separator);
            var inputParameters = JsonConvert.DeserializeObject<InputParameters>(File.ReadAllText(inputJsonPath));

            // run
            Run(inputParameters);
            Log.CloseAndFlush();
        }

        #endregion

        #region Run

        private static void Run(InputParameters inputParameters)
        {
            Logger.Information("Run started.");
            Logger.Information(Separator);

            var graph = new AssemblyDependencyGraph();

            var baseDirectory = string.IsNullOrEmpty(inputParameters.BaseDirectory)
                ? new DirectoryInfo(Environment.CurrentDirectory)
                : new DirectoryInfo(inputParameters.BaseDirectory);

            if (!baseDirectory.Exists)
                throw new DirectoryNotFoundException($"The base directory '{baseDirectory.FullName}' was not found.");

            // register assembly sources
            graph.EnsureNodeWithAssemblySourceOrPatterns(inputParameters.Assemblies, baseDirectory);

            // register main assembly
            graph.EnsureNodeWithAssemblySource(inputParameters.MainAssembly, baseDirectory);

            // register additional dependencies
            foreach (var additionalDependency in inputParameters.AdditionalDependencies ?? Enumerable.Empty<InputParameters.AdditionalDependency>())
            {
                var dependant = graph.EnsureNodeWithAssemblySource(additionalDependency.Dependant, baseDirectory);
                var dependencies = graph.EnsureNodeWithAssemblySourceOrPatterns(additionalDependency.Dependencies, baseDirectory);
                foreach (var dependency in dependencies)
                {
                    graph.RegisterDependency(dependant, dependency);
                }
            }

            // load assemblies from files
            foreach (var node in graph.GetNodesToLoadFromFile())
            {
                graph.LoadNodeFromFile(node);
            }

            // load assemblies from assembly names
            foreach (var node in graph.GetNodesToLoadFromName())
            {
                graph.LoadNodeFromName(node);
            }

            Logger.Information(Separator);
            Logger.Information("Entire graph processed.");

            // locate the main assembly
            var mainNode = graph.EnsureNodeWithAssemblySource(inputParameters.MainAssembly, baseDirectory);

            // write report
            WriteReport(mainNode, graph);

            Logger.Information(Separator);
            Logger.Information("Run finished.");
        }

        #endregion

        #region Report

        private static void WriteReport(AssemblyDependencyNode mainNode, AssemblyDependencyGraph graph)
        {
            Logger.Information(Separator);
            Logger.Information("Overview for [{AssemblyName}]", mainNode.Name);

            WriteDirectDependencies(mainNode, graph);
            WriteIndirectDependencies(mainNode, graph);
            WriteAllDependencies(mainNode, graph, out var allMainDependencies);

            var nodes = graph.GetAllNodes()
                .OrderBy(x => x.Identity)
                .ToList();

            WriteAssembliesNotLoaded(nodes);
            WriteAssembliesRedirectedUponLoad(nodes);
            WriteMainNodeDependants(mainNode, graph);
            WriteOtherLeafNodes(mainNode, graph, nodes);
            WriteNodesOutsideMainDependencyTree(mainNode, nodes, allMainDependencies);
            WriteBindingRedirects(allMainDependencies);
        }

        private static void WriteBindingRedirects(HashSet<AssemblyDependencyNode> allMainDependencies)
        {
            var dependenciesGrouped = allMainDependencies
                .GroupBy(x => x.Identity.Unversioned)
                .Where(x => x.Count() > 1 && x.Any(y => y.Loaded))
                .OrderBy(x => x.Key)
                .ToList();

            if (dependenciesGrouped.Any())
            {
                Logger.Information(Separator);
                Logger.Information("Recommended binding redirects:");
                var dependentAssemblyTriples = new List<(AssemblyUnversionedIdentity Key, Version highestVersion, Version highestVersionLoaded)>();

                foreach (var group in dependenciesGrouped)
                {
                    Logger.Information(Separator);
                    Logger.Information("{AssemblyName}", group.Key.ToString());

                    Logger.Information("Versions:");
                    foreach (var node in group.OrderBy(x => x.Identity.Version))
                    {
                        if (node.Loaded)
                        {
                            Logger.Information("{Version} [Location=\"{Location}\"]", node.Identity.Version, node.File.FullName);
                        }
                        else
                        {
                            Logger.Warning("{Version} [Not Found]", node.Identity.Version);
                        }
                    }

                    Logger.Information("Recommended redirect:");
                    var highestVersion = group.Max(x => x.Identity.Version);
                    var highestVersionLoaded = group.Where(x => x.Loaded).Max(x => x.Identity.Version);

                    if (highestVersionLoaded < highestVersion)
                    {
                        Logger.Warning("WARNING: Recommending a downgrading redirect.");
                    }

                    var dependentAssemblyTriple = (group.Key, highestVersion, highestVersionLoaded);
                    var element = GetAssemblyBindingXElement(dependentAssemblyTriple);
                    Logger.Information("\n{Element}", element);
                    dependentAssemblyTriples.Add(dependentAssemblyTriple);
                }

                if (dependenciesGrouped.Count > 1)
                {
                    Logger.Information(Separator);
                    Logger.Information("Collected binding redirects:");
                    var element = GetAssemblyBindingXElement(dependentAssemblyTriples.ToArray());
                    Logger.Information("\n{Element}", element);
                }
            }
            else
            {
                Logger.Information(Separator);
                Logger.Information("No recommended binding redirects.");
            }
        }

        private static void WriteNodesOutsideMainDependencyTree(AssemblyDependencyNode mainNode, List<AssemblyDependencyNode> nodes, HashSet<AssemblyDependencyNode> allMainDependencies)
        {
            var nodesOutsideOfDependencyTree = nodes.Where(x => x != mainNode && !allMainDependencies.Contains(x)).ToList();
            if (nodesOutsideOfDependencyTree.Any())
            {
                Logger.Information(Separator);
                Logger.Information("All assemblies that aren't in any way connected to the main:");

                foreach (var node in nodesOutsideOfDependencyTree)
                {
                    Logger.Warning("{AssemblyName}", node.Name.FullName);
                }
            }
        }

        private static void WriteOtherLeafNodes(AssemblyDependencyNode mainNode, AssemblyDependencyGraph graph, List<AssemblyDependencyNode> nodes)
        {
            var otherLeafNodes = nodes.Where(x => x != mainNode && !graph.GetDirectDependants(x).Any()).ToList();
            if (otherLeafNodes.Any())
            {
                Logger.Information(Separator);
                Logger.Information("Other assemblies that don't have any dependants:");

                foreach (var node in otherLeafNodes)
                {
                    Logger.Warning("{AssemblyName}", node.Name.FullName);
                }

                Logger.Information("If these assemblies relate to the main assembly dynamically, you can add them as a manual reference on input.");
                Logger.Information("If these are not loaded dynamically, they might also redundant.");
            }
        }

        private static void WriteMainNodeDependants(AssemblyDependencyNode mainNode, AssemblyDependencyGraph graph)
        {
            var mainNodeDependants = graph.GetDirectDependants(mainNode).OrderBy(x => x.Identity).ToList();
            if (mainNodeDependants.Any())
            {
                Logger.Information(Separator);
                Logger.Warning("WARNING: Detected that there are still some assemblies that depend on the main assembly. Try to avoid this scenario. Different binding redirects might be needed for them.");
                foreach (var node in mainNodeDependants)
                {
                    Logger.Information("{AssemblyNameWithVersion}", GetSimpleName(node.Identity.Unversioned, new[] { node }));
                    Logger.Information("-- {AssemblyIdentity}", node.Identity);
                    Logger.Information("");
                }
            }
        }

        private static void WriteAssembliesRedirectedUponLoad(List<AssemblyDependencyNode> nodes)
        {
            var loadedNodesDifferentVersion = nodes.Where(x => x.Loaded && x.Name.FullName != x.Assembly.AssemblyName).ToList();
            if (loadedNodesDifferentVersion.Any())
            {
                Logger.Information(Separator);
                Logger.Information("Assemblies that were redirected to a different version upon loading:");

                foreach (var node in loadedNodesDifferentVersion)
                {
                    Logger.Information(Separator);
                    Logger.Warning("Requested: {AssemblyName}", node.Name.FullName);
                    Logger.Warning("Resolved to: {AssemblyName}", node.Assembly.AssemblyName);
                }
            }
        }

        private static void WriteAssembliesNotLoaded(List<AssemblyDependencyNode> nodes)
        {
            var nodesByGroup = nodes.ToLookup(x => x.Identity.Unversioned);
            var nodesNotLoaded = nodes.Where(node => !node.Loaded).ToList();
            if (nodesNotLoaded.Any())
            {
                Logger.Information(Separator);
                Logger.Information("Assemblies that couldn't be loaded:");

                foreach (var node in nodesNotLoaded)
                {
                    Logger.Information(Separator);

                    if (node.Name != null)
                    {
                        Logger.Information("{AssemblyNameWithVersion}", GetSimpleName(node.Identity.Unversioned, new[] { node }));
                        Logger.Information("-- {AssemblyIdentity}", node.Identity);
                    }
                    else
                    {
                        Logger.Information("{File}", node.File.FullName);
                    }

                    Logger.Information("");

                    if (node.LoadedFromName == AssemblyLoadStatus.Failed)
                    {
                        Logger.Information("Couldn't load from assembly name. Exception message: {ExceptionMessage}", node.LoadedFromNameError.Message);
                    }
                    else if (node.LoadedFromFile == AssemblyLoadStatus.Failed)
                    {
                        Logger.Information("Couldn't load from file. Exception message: {ExceptionMessage}", node.LoadedFromFileError.Message);
                    }
                    else
                    {
                        throw new InvalidOperationException("Assembly not attempted to be loaded, something went wrong.");
                    }

                    Logger.Information("If this is an important reference, consider fixing this reference. Also make sure that you didn't omit it from the inputs.");

                    var otherNodesInGroup = nodesByGroup[node.Identity.Unversioned].Where(x => x != node && x.Loaded).ToList();
                    if (otherNodesInGroup.Any())
                    {
                        Logger.Information("Other versions of this assembly were loaded:");
                        foreach (var otherVersionNode in otherNodesInGroup.OrderBy(x => x.Identity.Version))
                        {
                            Logger.Information("{Version}", otherVersionNode.Identity.Version);
                        }

                        if (otherNodesInGroup.All(x => x.Identity.Version < node.Identity.Version))
                        {
                            Logger.Warning("WARNING: This is the highest version of the assembly, yet it wasn't loaded. A downgrading binding redirect might be needed.");
                        }
                    }
                }
            }
        }

        private static void WriteAllDependencies(AssemblyDependencyNode mainNode, AssemblyDependencyGraph graph, out HashSet<AssemblyDependencyNode> allMainDependencies)
        {
            Logger.Information(Separator);
            Logger.Information("All dependencies:");
            Logger.Information("");

            allMainDependencies = graph.GetAllDependencies(mainNode).ToHashSet();
            foreach (var dependencyGroup in allMainDependencies.GroupBy(x => x.Identity.Unversioned).OrderBy(x => x.Key))
            {
                Logger.Information("{AssemblyNameWithVersion}", GetSimpleName(dependencyGroup.Key, dependencyGroup.ToList()));
                foreach (var node in dependencyGroup)
                {
                    Logger.Information("-- {AssemblyIdentity}", node.Identity);
                }

                Logger.Information("");
            }
        }

        private static void WriteIndirectDependencies(AssemblyDependencyNode mainNode, AssemblyDependencyGraph graph)
        {
            Logger.Information(Separator);
            Logger.Information("Indirect dependencies:");
            Logger.Information("");

            foreach (var dependencyGroup in graph.GetIndirectDependencies(mainNode).GroupBy(x => x.Identity.Unversioned).OrderBy(x => x.Key))
            {
                Logger.Information("{AssemblyNameWithVersion}", GetSimpleName(dependencyGroup.Key, dependencyGroup.ToList()));
                foreach (var node in dependencyGroup)
                {
                    Logger.Information("-- {AssemblyIdentity}", node.Identity);
                }

                Logger.Information("");
            }
        }

        private static void WriteDirectDependencies(AssemblyDependencyNode mainNode, AssemblyDependencyGraph graph)
        {
            Logger.Information(Separator);
            Logger.Information("Direct dependencies:");
            Logger.Information("");

            foreach (var dependencyGroup in graph.GetDirectDependencies(mainNode).GroupBy(x => x.Identity.Unversioned).OrderBy(x => x.Key))
            {
                Logger.Information("{AssemblyNameWithVersion}", GetSimpleName(dependencyGroup.Key, dependencyGroup.ToList()));
                foreach (var node in dependencyGroup)
                {
                    Logger.Information("-- {AssemblyIdentity}", node.Identity);
                }

                Logger.Information("");
            }
        }

        #endregion

        #region Helpers

        private static readonly string Separator = new string('-', 20);
        private static readonly Version VersionZero = new Version(0, 0, 0, 0);
        private static readonly XNamespace AsmV1XNamespace = "urn:schemas-microsoft-com:asm.v1";

        public static XElement GetAssemblyBindingXElement(params (AssemblyUnversionedIdentity unversionedIdentity, Version highestOldVersion, Version newVersion)[] dependentAssemblies)
        {
            var ns = AsmV1XNamespace;
            var assemblyBindingElement = new XElement(ns + "assemblyBinding");

            foreach (var dependentAssembly in dependentAssemblies)
            {
                var dependentAssemlyElement = GetDependentAssemblyXElement(dependentAssembly.unversionedIdentity, dependentAssembly.highestOldVersion, dependentAssembly.newVersion);
                assemblyBindingElement.Add(dependentAssemlyElement);
            }

            return assemblyBindingElement;
        }

        private static XElement GetDependentAssemblyXElement(AssemblyUnversionedIdentity unversionedIdentity, Version highestOldVersion, Version newVersion)
        {
            var ns = AsmV1XNamespace;
            var dependentAssemlyElement = new XElement(ns + "dependentAssembly");

            var assemblyIdentityElement = new XElement(ns + "assemblyIdentity");
            assemblyIdentityElement.Add(new XAttribute("name", unversionedIdentity.Name));
            assemblyIdentityElement.Add(new XAttribute("publicKeyToken", unversionedIdentity.PublicKeyToken));
            assemblyIdentityElement.Add(new XAttribute("culture", unversionedIdentity.Culture));
            dependentAssemlyElement.Add(assemblyIdentityElement);

            var bindingRedirectElement = new XElement(ns + "bindingRedirect");
            bindingRedirectElement.Add(new XAttribute("oldVersion", $"{VersionZero}-{highestOldVersion}"));
            bindingRedirectElement.Add(new XAttribute("newVersion", $"{newVersion}"));
            dependentAssemlyElement.Add(bindingRedirectElement);
            return dependentAssemlyElement;
        }

        public static string GetSimpleName(AssemblyUnversionedIdentity unversionedIdentity, IList<AssemblyDependencyNode> nodes)
        {
            var versionString = string.Empty;
            if (nodes.Any(x => x.Identity.Version != VersionZero))
            {
                var versionsOrdered = nodes.Select(x => x.Identity.Version).OrderBy(x => x);
                var versionsJoined = string.Join(", ", versionsOrdered.Select(x => x.ToString()));
                versionString = $" [{versionsJoined}]";
            }

            var result = $"{unversionedIdentity.Name}{versionString}";
            return result;
        }

        #endregion
    }
}