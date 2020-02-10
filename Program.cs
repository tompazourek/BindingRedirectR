﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Serilog;
using static System.Console;

namespace BindingRedirectR
{
    internal class Program
    {
        private static readonly ILogger Log = (Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .CreateLogger())
            .ForContext<Program>();

        private static readonly string Separator = new string('-', 20);
        private static readonly Version VersionZero = new Version(0, 0, 0, 0);

        private static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Error(e.ExceptionObject as Exception, "Unhandled exception occurred.");
                Log.Information("Press any key to exit.");
                Serilog.Log.CloseAndFlush();
                ReadKey();
                Environment.Exit(1);
            };

            var inputParameters = SampleInputs.Foo;
            Run(inputParameters);

            Log.Information(Separator);
            Log.Information("Press any key to exit.");
            Serilog.Log.CloseAndFlush();
            ReadKey();
        }

        private static void Run(InputParameters inputParameters)
        {
            Log.Information("Run started.");
            Log.Information(Separator);

            var graph = new AssemblyMGraph();

            // register assembly sources
            foreach (var assemblySource in inputParameters.AssemblySources)
            {
                graph.EnsureNodeWithAssemblySource(assemblySource);
            }

            // register main assembly
            graph.EnsureNodeWithAssemblySource(inputParameters.MainAssemblySource);

            // register manual references
            foreach (var (dependantSource, dependencySource) in inputParameters.ManualReferences)
            {
                var dependant = graph.EnsureNodeWithAssemblySource(dependantSource);
                var dependency = graph.EnsureNodeWithAssemblySource(dependencySource);
                graph.RegisterDependency(dependant, dependency);
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

            Log.Information(Separator);
            Log.Information("Entire graph processed.");

            // locate the main assembly
            var mainNode = graph.EnsureNodeWithAssemblySource(inputParameters.MainAssemblySource);

            Log.Information(Separator);
            Log.Information("Overview for [{AssemblyName}]", mainNode.Name);

            Log.Information(Separator);
            Log.Information("Direct references:");
            foreach (var dependencyGroup in graph.GetDirectDependencies(mainNode).GroupBy(x => x.Identity.Group).OrderBy(x => x.Key.ToString()))
            {
                Log.Information("{AssemblyNameWithVersion}", GetSimpleName(dependencyGroup.Key, dependencyGroup.ToList()));
            }

            Log.Information(Separator);
            Log.Information("Indirect references:");
            foreach (var dependencyGroup in graph.GetIndirectDependencies(mainNode).GroupBy(x => x.Identity.Group).OrderBy(x => x.Key.ToString()))
            {
                Log.Information("{AssemblyNameWithVersion}", GetSimpleName(dependencyGroup.Key, dependencyGroup.ToList()));
            }

            Log.Information(Separator);
            Log.Information("All references:");
            var allMainDependencies = graph.GetAllDependencies(mainNode).ToHashSet();
            foreach (var dependencyGroup in allMainDependencies.GroupBy(x => x.Identity.Group).OrderBy(x => x.Key.ToString()))
            {
                Log.Information("{AssemblyNameWithVersion}", GetSimpleName(dependencyGroup.Key, dependencyGroup.ToList()));
            }

            var nodes = graph.GetAllNodes().OrderBy(x => x.Identity.Group.ToString()).ThenBy(x => x.Identity.Version).ToList();
            var nodesByGroup = nodes.ToLookup(x => x.Identity.Group);

            var nodesNotLoaded = nodes.Where(node => !node.Loaded).ToList();
            if (nodesNotLoaded.Any())
            {
                Log.Information(Separator);
                Log.Information("Assemblies that couldn't be loaded:");

                foreach (var node in nodesNotLoaded)
                {
                    Log.Information(Separator);

                    if (node.Name != null)
                    {
                        Log.Information("{AssemblyNameWithVersion}", GetSimpleName(node.Identity.Group, new[] { node }));
                    }
                    else
                    {
                        Log.Information("{File}", node.File.FullName);
                    }

                    if (node.LoadedFromName == AssemblyLoadStatus.Failed)
                    {
                        Log.Information("Couldn't load from assembly name. Exception message: {ExceptionMessage}", node.LoadedFromNameError.Message);
                    }
                    else if (node.LoadedFromFile == AssemblyLoadStatus.Failed)
                    {
                        Log.Information("Couldn't load from file. Exception message: {ExceptionMessage}", node.LoadedFromFileError.Message);
                    }
                    else
                    {
                        throw new InvalidOperationException("Assembly not attempted to be loaded, something went wrong.");
                    }

                    Log.Information("If this is an important reference, consider fixing this reference. Also make sure that you didn't omit it from the inputs.");

                    var otherNodesInGroup = nodesByGroup[node.Identity.Group].Where(x => x != node && x.Loaded).ToList();
                    if (otherNodesInGroup.Any())
                    {
                        Log.Information("Other versions of this assembly were loaded:");
                        foreach (var otherVersionNode in otherNodesInGroup.OrderBy(x => x.Identity.Version))
                        {
                            Log.Information("{Version}", otherVersionNode.Identity.Version);
                        }

                        if (otherNodesInGroup.All(x => x.Identity.Version < node.Identity.Version))
                        {
                            Log.Warning("WARNING: This is the highest version of the assembly, yet it wasn't loaded. A downgrading binding redirect might be needed.");
                        }
                    }
                }
            }

            var loadedNodesDifferentVersion = nodes.Where(x => x.Loaded && x.Name.FullName != x.Assembly.AssemblyName).ToList();
            if (loadedNodesDifferentVersion.Any())
            {
                Log.Information(Separator);
                Log.Information("Assemblies that resolved a different version upon loading:");

                foreach (var node in loadedNodesDifferentVersion)
                {
                    Log.Information(Separator);
                    Log.Warning("Requested: {AssemblyName}", node.Name.FullName);
                    Log.Warning("Resolved to: {AssemblyName}", node.Assembly.AssemblyName);
                }
            }

            var mainNodeDependants = graph.GetDirectDependants(mainNode).OrderBy(x => x.Identity.Group.ToString()).ThenBy(x => x.Identity.Version).ToList();
            if (mainNodeDependants.Any())
            {
                Log.Information(Separator);
                Log.Warning("WARNING: Detected that there are still some assemblies that depend on the main assembly. Try to avoid this scenario. Different binding redirects might be needed for them.");
                foreach (var node in mainNodeDependants)
                {
                    Log.Information("{AssemblyNameWithVersion}", GetSimpleName(node.Identity.Group, new[] { node }));
                }
            }

            var otherLeafNodes = nodes.Where(x => x != mainNode && !graph.GetDirectDependants(x).Any()).ToList();
            if (otherLeafNodes.Any())
            {
                Log.Information(Separator);
                Log.Information("Other assemblies that don't have any dependants:");

                foreach (var node in otherLeafNodes)
                {
                    Log.Warning("{AssemblyName}", node.Name.FullName);
                }

                Log.Information("If these assemblies relate to the main assembly dynamically, you can add them as a manual reference on input.");
                Log.Information("If these are not loaded dynamically, they might also redundant.");
            }

            var nodesOutsideOfDependencyTree = nodes.Where(x => x != mainNode && !allMainDependencies.Contains(x)).ToList();
            if (nodesOutsideOfDependencyTree.Any())
            {
                Log.Information(Separator);
                Log.Information("All assemblies that aren't in any way connected to the main:");

                foreach (var node in nodesOutsideOfDependencyTree)
                {
                    Log.Warning("{AssemblyName}", node.Name.FullName);
                }
            }

            var dependenciesGrouped = allMainDependencies
                .GroupBy(x => x.Identity.Group)
                .Where(x => x.Count() > 1 && x.Any(y => y.Loaded))
                .OrderBy(x => x.Key.ToString())
                .ToList();

            if (dependenciesGrouped.Any())
            {
                Log.Information(Separator);
                Log.Information("Recommended binding redirects:");

                foreach (var group in dependenciesGrouped)
                {
                    Log.Information(Separator);
                    Log.Information("{AssemblyName}", group.Key.ToString());

                    Log.Information("Versions:");
                    foreach (var node in group.OrderBy(x => x.Identity.Version))
                    {
                        if (node.Loaded)
                        {
                            Log.Information("{Version} [Location=\"{Location}\"]", node.Identity.Version, node.File.FullName);
                        }
                        else
                        {
                            Log.Warning("{Version} [Not Found]", node.Identity.Version);
                        }
                    }

                    Log.Information("Recommended redirect:");
                    var highestVersion = group.Max(x => x.Identity.Version);
                    var highestVersionLoaded = group.Where(x => x.Loaded).Max(x => x.Identity.Version);

                    if (highestVersionLoaded < highestVersion)
                    {
                        Log.Warning("WARNING: Recommending a downgrading redirect.");
                    }

                    var element = GetAssemblyBindingXElement(group.Key, highestVersion, highestVersionLoaded);
                    Log.Information("\n{Element}", element);
                }
            }
            else
            {
                Log.Information(Separator);
                Log.Information("No recommended binding redirects.");
            }

            Log.Information(Separator);
            Log.Information("Run finished.");
        }

        public static XElement GetAssemblyBindingXElement(AssemblyGroupIdentity groupIdentity, Version highestOldVersion, Version newVersion)
        {
            XNamespace ns = "urn:schemas-microsoft-com:asm.v1";
            var assemblyBindingElement = new XElement(ns + "assemblyBinding");
            var dependentAssemlyElement = new XElement(ns + "dependentAssembly");
            
            var assemblyIdentityElement = new XElement(ns + "assemblyIdentity");
            assemblyIdentityElement.Add(new XAttribute("name", groupIdentity.Name));
            assemblyIdentityElement.Add(new XAttribute("publicKeyToken", groupIdentity.PublicKeyToken));
            assemblyIdentityElement.Add(new XAttribute("culture", groupIdentity.Culture));
            dependentAssemlyElement.Add(assemblyIdentityElement);

            var bindingRedirectElement = new XElement(ns + "bindingRedirect");
            bindingRedirectElement.Add(new XAttribute("oldVersion", $"{VersionZero}-{highestOldVersion}"));
            bindingRedirectElement.Add(new XAttribute("newVersion", $"{newVersion}"));
            dependentAssemlyElement.Add(bindingRedirectElement);

            assemblyBindingElement.Add(dependentAssemlyElement);
            return assemblyBindingElement;
        }

        public static string GetSimpleName(AssemblyGroupIdentity groupIdentity, IList<AssemblyMNode> nodes)
            => $"{groupIdentity.Name}"
               + (
                   nodes.Any(x => x.Identity.Version != VersionZero)
                       ? " [" + string.Join(", ", nodes
                             .Select(x => x.Identity.Version)
                             .OrderBy(x => x)
                             .Select(x => x.ToString())) + "]"
                       : "");

        public static void ProcessNode(AssemblyNode node)
        {
            Log.Information(Separator);
            Log.Information(Separator);

            Log.Information("Processing dependency [{AssemblyIdentity}].", node.AssemblyGroupIdentity);

            var directDependants = node.Dependants;
            var dependants = node.GetDependantsIncludingTransient()
                .OrderBy(x => x.TargetNode.AssemblyGroupIdentity.Name)
                .ThenBy(x => x.TargetNode.AssemblyGroupIdentity.Culture)
                .ThenBy(x => x.TargetNode.AssemblyGroupIdentity.PublicKeyToken)
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
                foreach (var assemblyIdentity in versionGroupDirect.Select(x => x.TargetNode.AssemblyGroupIdentity).ToHashSet())
                {
                    Log.Information("{AssemblyIdentity}", assemblyIdentity);
                }

                Log.Information("[{Version}] Assemblies with transient dependencies:", versionGroup.Key);
                var versionGroupTransient = versionGroup.Where(x => !directDependants.Contains(x)).ToList();
                foreach (var assemblyIdentity in versionGroupTransient.Select(x => x.TargetNode.AssemblyGroupIdentity).ToHashSet())
                {
                    Log.Information("{AssemblyIdentity}", assemblyIdentity);
                }
            }

            Log.Information(Separator);

            Log.Information("Results for dependency [{AssemblyIdentity}].", node.AssemblyGroupIdentity);

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
                    .Select(x => x.TargetNode.AssemblyGroupIdentity)
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
                        .Select(x => x.TargetNode.AssemblyGroupIdentity)
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