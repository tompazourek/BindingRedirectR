﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Serilog;

namespace BindingRedirectR
{
    internal class AssemblyDependencyGraph
    {
        #region Public members

        public IList<AssemblyDependencyNode> EnsureNodeWithAssemblySourceOrPatterns(IList<string> assemblySources, DirectoryInfo baseDirectory)
        {
            var allNodes = new List<AssemblyDependencyNode>();

            foreach (var assemblySource in assemblySources)
            {
                var nodes = EnsureNodeWithAssemblySourceOrPattern(assemblySource, baseDirectory);
                allNodes.AddRange(nodes);
            }

            allNodes = allNodes.Distinct().ToList();
            return allNodes;
        }

        public AssemblyDependencyNode EnsureNodeWithAssemblySource(string assemblySource, DirectoryInfo baseDirectory)
        {
            if (IsValidAssemblyString(assemblySource, out var name))
                return EnsureNodeWithName(name);

            if (IsValidPath(assemblySource, baseDirectory, out var fileInfo))
                return EnsureNodeWithFile(fileInfo);

            throw new ArgumentException($"Assembly source '{assemblySource}' is not valid, it's neither a path, nor an assembly string.", nameof(assemblySource));
        }

        public void RegisterDependency(AssemblyDependencyNode dependant, AssemblyDependencyNode dependency)
        {
            _dependencyByNode.AddOrUpdate(dependant, _ => new HashSet<AssemblyDependencyNode> { dependency }, (_, set) =>
            {
                set.Add(dependency);
                return set;
            });

            _dependantByNode.AddOrUpdate(dependency, _ => new HashSet<AssemblyDependencyNode> { dependant }, (_, set) =>
            {
                set.Add(dependant);
                return set;
            });
        }

        public IEnumerable<AssemblyDependencyNode> GetNodesToLoadFromFile()
            => new NodesToLoadEnumerable(() => _nodes, x => x.LoadedFromFile == AssemblyLoadStatus.NotAttempted && !x.Loaded && x.File != null);

        public IEnumerable<AssemblyDependencyNode> GetNodesToLoadFromName()
            => new NodesToLoadEnumerable(() => _nodes, x => x.LoadedFromName == AssemblyLoadStatus.NotAttempted && !x.Loaded && x.Name != null);

        public void LoadNodeFromFile(AssemblyDependencyNode node)
        {
            if (node.File == null)
                throw new InvalidOperationException("Cannot load assembly from file, the file is empty.");

            switch (node.LoadedFromFile)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot load assembly from file, it's already loaded.");
                case AssemblyLoadStatus.Failed:
                    throw new InvalidOperationException("Cannot load assembly from file, previous attempt failed.");
            }

            if (node.Loaded)
                throw new InvalidOperationException("Cannot load assembly from file, it's already been loaded.");

            var fileFullName = node.File.FullName;
            try
            {
                Logger.Debug("Loading from file {File}.", fileFullName);
                var assembly = AssemblyMetadataLoader.LoadFrom(fileFullName);
                node.MarkAsLoadedFromFile(assembly);
                ProcessLoadedNode(node);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to load from file {File}.", fileFullName);
                node.MarkAsFailedFromFile(ex);
            }
        }

        public void LoadNodeFromName(AssemblyDependencyNode node)
        {
            if (node.Name == null)
                throw new InvalidOperationException("Cannot load assembly from name, the name is empty.");

            switch (node.LoadedFromName)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot load assembly from name, it's already loaded.");
                case AssemblyLoadStatus.Failed:
                    throw new InvalidOperationException("Cannot load assembly from name, previous attempt failed.");
            }

            if (node.Loaded)
                throw new InvalidOperationException("Cannot load assembly from name, it's already been loaded.");

            var assemblyString = node.Name.ToString();

            try
            {
                Logger.Debug("Loading from name {AssemblyName}.", assemblyString);
                var assembly = AssemblyMetadataLoader.Load(node.Name);
                if (assembly.AssemblyName != node.Name.FullName)
                {
                    Logger.Warning("Requesting the load of [{RequestedAssemblyName}], but obtained [{LoadedAssemblyName}].", node.Name.FullName, assembly.AssemblyName);
                }

                node.MarkAsLoadedFromName(assembly);
                ProcessLoadedNode(node);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to load from name {AssemblyName}.", assemblyString);
                node.MarkAsFailedFromName(ex);
            }
        }

        public IEnumerable<AssemblyDependencyNode> GetDirectDependants(AssemblyDependencyNode dependency)
            => _dependantByNode.TryGetValue(dependency, out var result)
                ? result
                : Enumerable.Empty<AssemblyDependencyNode>();

        public IEnumerable<AssemblyDependencyNode> GetDirectDependantsByGroup(AssemblyUnversionedIdentity dependencyGroup) 
            => _nodes
                .Where(x => x.Identity.Unversioned == dependencyGroup)
                .SelectMany(GetDirectDependants)
                .Distinct();

        public IEnumerable<AssemblyDependencyNode> GetDirectDependencies(AssemblyDependencyNode dependant)
            => _dependencyByNode.TryGetValue(dependant, out var result)
                ? result
                : Enumerable.Empty<AssemblyDependencyNode>();

        public IEnumerable<AssemblyDependencyNode> GetIndirectDependants(AssemblyDependencyNode dependency)
        {
            var results = new HashSet<AssemblyDependencyNode>(GetAllDependants(dependency));

            // keep only indirect
            results.ExceptWith(GetDirectDependants(dependency));

            return results;
        }

        public IEnumerable<AssemblyDependencyNode> GetIndirectDependencies(AssemblyDependencyNode dependant)
        {
            var results = new HashSet<AssemblyDependencyNode>(GetAllDependencies(dependant));

            // keep only indirect
            results.ExceptWith(GetDirectDependencies(dependant));

            return results;
        }
        
        public IEnumerable<AssemblyDependencyNode> GetAllDependants(AssemblyDependencyNode dependency)
        {
            var results = new HashSet<AssemblyDependencyNode>();
            var directDependants = GetDirectDependants(dependency).ToList();

            // collect both direct and indirect
            var nodesToProcess = new HashSet<AssemblyDependencyNode>(directDependants);
            var processedNodes = new HashSet<AssemblyDependencyNode>();
            while (nodesToProcess.Any())
            {
                var node = nodesToProcess.First();
                if (!processedNodes.Contains(node))
                {
                    results.Add(node);
                    foreach (var dependant in GetDirectDependants(node))
                    {
                        nodesToProcess.Add(dependant);
                    }

                    processedNodes.Add(node);
                }

                nodesToProcess.Remove(node);
            }

            return results;
        }

        public IEnumerable<AssemblyDependencyNode> GetAllDependencies(AssemblyDependencyNode dependant)
        {
            var results = new HashSet<AssemblyDependencyNode>();
            var directDependencies = GetDirectDependencies(dependant).ToList();

            // collect both direct and indirect
            var nodesToProcess = new HashSet<AssemblyDependencyNode>(directDependencies);
            var processedNodes = new HashSet<AssemblyDependencyNode>();
            while (nodesToProcess.Any())
            {
                var node = nodesToProcess.First();
                if (!processedNodes.Contains(node))
                {
                    results.Add(node);
                    foreach (var dependency in GetDirectDependencies(node))
                    {
                        nodesToProcess.Add(dependency);
                    }

                    processedNodes.Add(node);
                }

                nodesToProcess.Remove(node);
            }

            return results;
        }

        public IList<AssemblyDependencyNode> GetAllDependenciesIncludingEntireGroup(AssemblyDependencyNode dependant)
        {
            var allDependencies = GetAllDependencies(dependant);
            var allDependencyGroups = allDependencies.Select(x => x.Identity.Unversioned).ToHashSet();
            var allNodesByGroups = GetAllNodes().Where(x => allDependencyGroups.Contains(x.Identity.Unversioned)).ToList();
            return allNodesByGroups;
        }

        public IEnumerable<AssemblyDependencyNode> GetAllNodes() => _nodes;

        #endregion

        #region Private members

        private static readonly ILogger Logger = Log.ForContext<Program>();

        private readonly HashSet<AssemblyDependencyNode> _nodes = new HashSet<AssemblyDependencyNode>();
        private readonly ConcurrentDictionary<string, AssemblyDependencyNode> _nodeByName = new ConcurrentDictionary<string, AssemblyDependencyNode>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, AssemblyDependencyNode> _nodeByFile = new ConcurrentDictionary<string, AssemblyDependencyNode>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<AssemblyDependencyNode, HashSet<AssemblyDependencyNode>> _dependencyByNode = new ConcurrentDictionary<AssemblyDependencyNode, HashSet<AssemblyDependencyNode>>();
        private readonly ConcurrentDictionary<AssemblyDependencyNode, HashSet<AssemblyDependencyNode>> _dependantByNode = new ConcurrentDictionary<AssemblyDependencyNode, HashSet<AssemblyDependencyNode>>();

        private IList<AssemblyDependencyNode> EnsureNodeWithAssemblySourceOrPattern(string assemblySource, DirectoryInfo baseDirectory)
        {
            if (IsValidAssemblyString(assemblySource, out var name))
                return new[] { EnsureNodeWithName(name) };

            if (IsValidGlobPattern(assemblySource, baseDirectory, out var fileInfos))
            {
                var results = fileInfos.Select(EnsureNodeWithFile).Distinct().ToList();
                return results;
            }

            if (IsValidPath(assemblySource, baseDirectory, out var fileInfo))
                return new[] { EnsureNodeWithFile(fileInfo) };

            throw new ArgumentException($"Assembly source '{assemblySource}' is not valid, it's neither a path, nor an assembly string, nor a file globbing pattern.", nameof(assemblySource));
        }

        private AssemblyDependencyNode EnsureNodeWithName(AssemblyName name)
        {
            var assemblyFullName = GetKeyFromName(name);
            var node = _nodeByName.GetOrAdd(assemblyFullName, _ => AssemblyDependencyNode.CreateFromName(name));
            _nodes.Add(node);
            return node;
        }

        private AssemblyDependencyNode EnsureNodeWithFile(FileInfo file)
        {
            var fullPath = GetKeyFromFile(file);
            var node = _nodeByFile.GetOrAdd(fullPath, _ => AssemblyDependencyNode.CreateFromFile(file));
            _nodes.Add(node);
            return node;
        }

        private static string GetKeyFromName(AssemblyName name) => name.FullName;
        private static string GetKeyFromFile(FileInfo file) => Path.GetFullPath(file.FullName);

        private static bool IsValidAssemblyString(string assemblyString, out AssemblyName name)
        {
            try
            {
                var regex = new Regex(".*, Version=.*, Culture=.*, PublicKeyToken=.*", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
                if (!regex.IsMatch(assemblyString))
                {
                    name = null;
                    return false;
                }

                name = new AssemblyName(assemblyString);
                return true;
            }
            catch (Exception)
            {
                name = null;
                return false;
            }
        }

        private static bool IsValidPath(string path, DirectoryInfo baseDirectory, out FileInfo fileInfo)
        {
            try
            {
                var root = Path.GetPathRoot(path);
                var isPathFullyQualified = root?.StartsWith(@"\\") == true || root?.EndsWith(@"\") == true;
                if (isPathFullyQualified)
                {
                    fileInfo = new FileInfo(path);
                    return true;
                }

                var fullyQualifiedPath = Path.Combine(baseDirectory.FullName, path ?? string.Empty);
                fileInfo = new FileInfo(fullyQualifiedPath);
                return true;
            }
            catch (Exception)
            {
                fileInfo = null;
                return false;
            }
        }

        private static bool IsValidGlobPattern(string globPattern, DirectoryInfo baseDirectory, out IList<FileInfo> files)
        {
            files = null;

            if (!globPattern.Contains('*'))
                return false;

            PatternMatchingResult patternMatchingResult;
            try
            {
                var matcher = new Matcher(StringComparison.InvariantCultureIgnoreCase);
                matcher.AddInclude(globPattern);
                patternMatchingResult = matcher.Execute(new DirectoryInfoWrapper(baseDirectory));
            }
            catch
            {
                return false;
            }

            files = new List<FileInfo>();

            if (!patternMatchingResult.HasMatches)
                return true;

            foreach (var match in patternMatchingResult.Files)
            {
                if (IsValidPath(match.Path, baseDirectory, out var fileInfo))
                {
                    files.Add(fileInfo);
                }
            }

            return true;
        }

        private void ProcessLoadedNode(AssemblyDependencyNode node)
        {
            // process name
            var nameKey = GetKeyFromName(node.Name);
            AssemblyDependencyNode previousNodeByName = null;
            _nodeByName.AddOrUpdate(nameKey, _ => node, (_, previousNode) =>
            {
                previousNodeByName = previousNode;
                return node;
            });

            if (previousNodeByName != null && previousNodeByName != node)
            {
                MergeNodeDependencies(node, previousNodeByName);
            }

            // process file
            var fileKey = GetKeyFromFile(node.File);
            AssemblyDependencyNode previousNodeByFile = null;
            _nodeByFile.AddOrUpdate(fileKey, _ => node, (_, previousNode) =>
            {
                previousNodeByFile = previousNode;
                return node;
            });

            if (previousNodeByFile != null && previousNodeByFile != node)
            {
                if (previousNodeByFile.Identity != node.Identity)
                {
                    Logger.Warning("There will be multiple nodes with the same file [{File}].", node.File.FullName);
                }
                else
                {
                    MergeNodeDependencies(node, previousNodeByFile);
                }
            }

            // process dependencies
            RegisterNodeDependencies(node);
        }

        private void RegisterNodeDependencies(AssemblyDependencyNode dependant)
        {
            foreach (var dependencyName in dependant.Assembly.ReferenceAssemblyNames)
            {
                var dependency = EnsureNodeWithName(new AssemblyName(dependencyName));
                RegisterDependency(dependant, dependency);
            }
        }

        private void MergeNodeDependencies(AssemblyDependencyNode targetNode, AssemblyDependencyNode sourceNode)
        {
            if (targetNode == sourceNode)
                throw new InvalidOperationException("Cannot merge nodes that are the same.");

            // find occurences of source node in internal dictionaries and redirect references to target node

            _nodes.Remove(sourceNode);

            if (sourceNode.Name != null)
            {
                var nameKey = GetKeyFromName(sourceNode.Name);
                if (_nodeByName.TryRemove(nameKey, out _))
                {
                    _nodeByName[nameKey] = targetNode;
                }
            }

            if (sourceNode.File != null)
            {
                var fileKey = GetKeyFromFile(sourceNode.File);
                if (_nodeByFile.TryRemove(fileKey, out _))
                {
                    _nodeByFile[fileKey] = targetNode;
                }
            }

            if (_dependencyByNode.TryRemove(sourceNode, out var sourceNodeDependencies))
            {
                foreach (var dependency in sourceNodeDependencies)
                {
                    _dependantByNode[dependency].Remove(sourceNode);
                    RegisterDependency(targetNode, dependency);
                }
            }

            if (_dependantByNode.TryRemove(sourceNode, out var sourceNodeDependants))
            {
                foreach (var dependant in sourceNodeDependants)
                {
                    _dependencyByNode[dependant].Remove(sourceNode);
                    RegisterDependency(dependant, targetNode);
                }
            }
        }

        #endregion

        #region Nested types

        private class NodesToLoadEnumerable : IEnumerable<AssemblyDependencyNode>
        {
            private readonly Func<HashSet<AssemblyDependencyNode>> _nodesAccessor;
            private readonly Func<AssemblyDependencyNode, bool> _filter;

            internal NodesToLoadEnumerable(Func<HashSet<AssemblyDependencyNode>> nodesAccessor, Func<AssemblyDependencyNode, bool> filter)
            {
                _nodesAccessor = nodesAccessor;
                _filter = filter;
            }

            public IEnumerator<AssemblyDependencyNode> GetEnumerator() => new NodesToLoadEnumerator(_nodesAccessor, _filter);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class NodesToLoadEnumerator : IEnumerator<AssemblyDependencyNode>
        {
            private readonly Func<HashSet<AssemblyDependencyNode>> _nodesAccessor;
            private readonly Func<AssemblyDependencyNode, bool> _filter;
            private HashSet<AssemblyDependencyNode> _processedNodes = new HashSet<AssemblyDependencyNode>();

            public NodesToLoadEnumerator(Func<HashSet<AssemblyDependencyNode>> nodesAccessor, Func<AssemblyDependencyNode, bool> filter)
            {
                _nodesAccessor = nodesAccessor;
                _filter = filter;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                var nodesToProcess = new HashSet<AssemblyDependencyNode>(_nodesAccessor());
                nodesToProcess.ExceptWith(_processedNodes);

                foreach (var node in nodesToProcess)
                {
                    _processedNodes.Add(node);

                    if (!_filter(node))
                        continue;

                    Current = node;
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                _processedNodes = new HashSet<AssemblyDependencyNode>();
            }

            public AssemblyDependencyNode Current { get; private set; }

            object IEnumerator.Current => Current;
        }

        #endregion
    }
}