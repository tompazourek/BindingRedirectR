using System;
using System.Collections.Generic;
using System.Linq;

namespace BindingRedirectR
{
    internal static class AssemblyNodeExtensions
    {
        public static ISet<AssemblyEdge> GetDependantsIncludingTransient(this AssemblyNode node)
        {
            var results = new HashSet<AssemblyEdge>();

            var nodeSet = new HashSet<(AssemblyNode, Version)> { (node, default) };
            while (nodeSet.Any())
            {
                var currentPair = nodeSet.First();
                var currentNode = currentPair.Item1;
                var currentTransientVersion = currentPair.Item2;

                foreach (var edge in currentNode.Dependants)
                {
                    var sourceVersion = currentNode.Equals(node) ? edge.SourceVersion : currentTransientVersion;
                    var transientEdge = new AssemblyEdge(edge.TargetNode, edge.TargetVersion, sourceVersion);
                    if (results.Contains(transientEdge))
                        continue;

                    results.Add(transientEdge);
                    nodeSet.Add((transientEdge.TargetNode, transientEdge.SourceVersion));
                }

                nodeSet.Remove(currentPair);
            }

            return results;
        }

        public static void VisitNodeAndDependants(this AssemblyNode node, Action<AssemblyNode> visitor)
        {
            var visited = new HashSet<AssemblyNode>();
            var toVisit = new HashSet<AssemblyNode> { node };

            while (toVisit.Any())
            {
                var currentNode = toVisit.First();

                visitor(currentNode);
                visited.Add(currentNode);
                toVisit.Remove(currentNode);

                foreach (var dependantNode in currentNode.Dependants.Select(x => x.TargetNode))
                {
                    if (visited.Contains(dependantNode))
                        continue;

                    toVisit.Add(dependantNode);
                }
            }
        }
    }
}