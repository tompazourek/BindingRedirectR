using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Serilog;

namespace BindingRedirectR
{
    internal class AssemblyMGraph
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<Program>();

        public AssemblyMNode EnsureNodeWithAssemblySource(string assemblySource)
        {
            if (IsValidAssemblyString(assemblySource, out var name))
                return EnsureNodeWithName(name);

            if (IsValidPath(assemblySource, out var fileInfo))
                return EnsureNodeWithFile(fileInfo);

            throw new ArgumentException($"Assembly source '{assemblySource}' is not valid, it's neither a path, nor an assembly string.", nameof(assemblySource));
        }

        private static bool IsValidAssemblyString(string assemblyString, out AssemblyName name)
        {
            try
            {
                name = new AssemblyName(assemblyString);
                return true;
            }
            catch (Exception)
            {
                name = null;
                return false;
            }
        }

        private static bool IsValidPath(string path, out FileInfo fileInfo)
        {
            try
            {
                fileInfo = new FileInfo(path);
                return true;
            }
            catch (Exception)
            {
                fileInfo = null;
                return false;
            }
        }

        private readonly HashSet<AssemblyMNode> Nodes = new HashSet<AssemblyMNode>();

        private readonly ConcurrentDictionary<string, AssemblyMNode> NodeByName = new ConcurrentDictionary<string, AssemblyMNode>(StringComparer.OrdinalIgnoreCase);
        private string GetKeyFromName(AssemblyName name) => name.FullName;

        public AssemblyMNode EnsureNodeWithName(AssemblyName name)
        {
            var assemblyFullName = GetKeyFromName(name);
            var node = NodeByName.GetOrAdd(assemblyFullName, _ => AssemblyMNode.CreateFromName(name));
            Nodes.Add(node);
            return node;
        }

        private readonly ConcurrentDictionary<string, AssemblyMNode> NodeByFile = new ConcurrentDictionary<string, AssemblyMNode>(StringComparer.OrdinalIgnoreCase);
        private string GetKeyFromFile(FileInfo file) => Path.GetFullPath(file.FullName);

        public AssemblyMNode EnsureNodeWithFile(FileInfo file)
        {
            var fullPath = GetKeyFromFile(file);
            var node = NodeByFile.GetOrAdd(fullPath, _ => AssemblyMNode.CreateFromFile(file));
            Nodes.Add(node);
            return node;
        }

        private readonly ConcurrentDictionary<AssemblyMNode, HashSet<AssemblyMNode>> DependencyByNode = new ConcurrentDictionary<AssemblyMNode, HashSet<AssemblyMNode>>();
        private readonly ConcurrentDictionary<AssemblyMNode, HashSet<AssemblyMNode>> DependantByNode = new ConcurrentDictionary<AssemblyMNode, HashSet<AssemblyMNode>>();

        public void RegisterDependency(AssemblyMNode dependant, AssemblyMNode dependency)
        {
            DependencyByNode.AddOrUpdate(dependant, _ => new HashSet<AssemblyMNode> { dependency }, (_, set) =>
            {
                set.Add(dependency);
                return set;
            });

            DependantByNode.AddOrUpdate(dependency, _ => new HashSet<AssemblyMNode> { dependant }, (_, set) =>
            {
                set.Add(dependant);
                return set;
            });
        }

        public IEnumerable<AssemblyMNode> GetNodesToLoadFromFile()
            => new NodesToLoadEnumerable(() => Nodes, x => x.LoadedFromFile == AssemblyLoadStatus.NotAttempted && !x.Loaded && x.File != null);

        public IEnumerable<AssemblyMNode> GetNodesToLoadFromName()
            => new NodesToLoadEnumerable(() => Nodes, x => x.LoadedFromName == AssemblyLoadStatus.NotAttempted && !x.Loaded && x.Name != null);

        private class NodesToLoadEnumerable : IEnumerable<AssemblyMNode>
        {
            private readonly Func<HashSet<AssemblyMNode>> _nodesAccessor;
            private readonly Func<AssemblyMNode, bool> _filter;

            public NodesToLoadEnumerable(Func<HashSet<AssemblyMNode>> nodesAccessor, Func<AssemblyMNode, bool> filter)
            {
                _nodesAccessor = nodesAccessor;
                _filter = filter;
            }

            public IEnumerator<AssemblyMNode> GetEnumerator() => new NodesToLoadEnumerator(_nodesAccessor, _filter);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class NodesToLoadEnumerator : IEnumerator<AssemblyMNode>
        {
            private readonly Func<HashSet<AssemblyMNode>> _nodesAccessor;
            private readonly Func<AssemblyMNode, bool> _filter;
            private HashSet<AssemblyMNode> _processedNodes = new HashSet<AssemblyMNode>();

            public NodesToLoadEnumerator(Func<HashSet<AssemblyMNode>> nodesAccessor, Func<AssemblyMNode, bool> filter)
            {
                _nodesAccessor = nodesAccessor;
                _filter = filter;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                var nodesToProcess = new HashSet<AssemblyMNode>(_nodesAccessor());
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
                _processedNodes = new HashSet<AssemblyMNode>();
            }

            public AssemblyMNode Current { get; private set; }

            object IEnumerator.Current => Current;
        }

        public void LoadNodeFromFile(AssemblyMNode node)
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
                Log.Debug("Loading from file {File}.", fileFullName);
                var assembly = Assembly.ReflectionOnlyLoadFrom(fileFullName);
                node.MarkAsLoadedFromFile(assembly);
                ProcessLoadedNode(node);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load from file {File}.", fileFullName);
                node.MarkAsFailedFromFile(ex);
            }
        }

        public void LoadNodeFromName(AssemblyMNode node)
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
                Log.Debug("Loading from name {AssemblyName}.", assemblyString);
                var assembly = Assembly.ReflectionOnlyLoad(assemblyString);
                node.MarkAsLoadedFromName(assembly);
                ProcessLoadedNode(node);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load from name {AssemblyName}.", assemblyString);
                node.MarkAsFailedFromName(ex);
            }
        }

        private void ProcessLoadedNode(AssemblyMNode node)
        {
            // process name
            var nameKey = GetKeyFromName(node.Name);
            AssemblyMNode previousNodeByName = null;
            NodeByName.AddOrUpdate(nameKey, _ => node, (_, previousNode) =>
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
            AssemblyMNode previousNodeByFile = null;
            NodeByFile.AddOrUpdate(fileKey, _ => node, (_, previousNode) =>
            {
                previousNodeByFile = previousNode;
                return node;
            });

            if (previousNodeByFile != null && previousNodeByFile != node)
            {
                MergeNodeDependencies(node, previousNodeByFile);
            }

            // process dependencies
            RegisterNodeDependencies(node);
        }

        private void RegisterNodeDependencies(AssemblyMNode dependant)
        {
            foreach (var dependencyName in dependant.Assembly.GetReferencedAssemblies())
            {
                var dependency = EnsureNodeWithName(dependencyName);
                RegisterDependency(dependant, dependency);
            }
        }

        private void MergeNodeDependencies(AssemblyMNode targetNode, AssemblyMNode sourceNode)
        {
            if (targetNode == sourceNode)
                throw new InvalidOperationException("Cannot merge nodes that are the same.");

            // find occurences of source node in internal dictionaries and redirect references to target node

            Nodes.Remove(sourceNode);

            if (sourceNode.Name != null)
            {
                var nameKey = GetKeyFromName(sourceNode.Name);
                if (NodeByName.TryRemove(nameKey, out _))
                {
                    NodeByName[nameKey] = targetNode;
                }
            }

            if (sourceNode.File != null)
            {
                var fileKey = GetKeyFromFile(sourceNode.File);
                if (NodeByFile.TryRemove(fileKey, out _))
                {
                    NodeByFile[fileKey] = targetNode;
                }
            }

            if (DependencyByNode.TryRemove(sourceNode, out var sourceNodeDependencies))
            {
                foreach (var dependency in sourceNodeDependencies)
                {
                    RegisterDependency(targetNode, dependency);
                }
            }

            if (DependantByNode.TryRemove(sourceNode, out var sourceNodeDependants))
            {
                foreach (var dependant in sourceNodeDependants)
                {
                    RegisterDependency(dependant, targetNode);
                }
            }
        }
    }
}