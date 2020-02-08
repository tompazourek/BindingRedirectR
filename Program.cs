using System;
using System.Linq;
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
            
            Log.Information(Separator);
            Log.Information("Run finished.");
        }

        private static void ProcessNode(AssemblyNode node)
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