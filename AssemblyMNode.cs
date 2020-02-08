using System;
using System.IO;
using System.Reflection;
using Serilog;

namespace BindingRedirectR
{
    internal class AssemblyMNode
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<Program>();

        public AssemblySpecificIdentity Identity { get; private set; }
        public Assembly Assembly { get; private set; }

        public AssemblyName Name { get; private set; }
        public AssemblyLoadStatus LoadedFromName { get; private set; }
        public Exception LoadedFromNameError { get; private set; }

        public FileInfo File { get; private set; }
        public AssemblyLoadStatus LoadedFromFile { get; private set; }
        public Exception LoadedFromFileError { get; private set; }

        public static AssemblyMNode CreateFromName(AssemblyName assemblyName)
        {
            Log.Information("Creating node from name {AssemblyName}.", assemblyName);
            return new AssemblyMNode
            {
                Name = assemblyName,
                Identity = new AssemblySpecificIdentity(assemblyName),
            };
        }

        public static AssemblyMNode CreateFromFile(FileInfo file)
        {
            Log.Information("Creating node from file {File}.", file.FullName);
            return new AssemblyMNode
            {
                File = file,
                // we don't have identity until we attempt to load
            };
        }

        public void MarkAsLoadedFromFile(Assembly assembly)
        {
            switch (LoadedFromFile)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as loaded from file, it's already loaded.");
                case AssemblyLoadStatus.Failed:
                    throw new InvalidOperationException("Cannot mark assembly as loaded from file, previous attempt failed.");
            }

            switch (LoadedFromName)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as loaded from file, it's already loaded from name.");
            }

            LoadedFromFile = AssemblyLoadStatus.Loaded;
            Assembly = assembly;
            Name = assembly.GetName();
            Identity = new AssemblySpecificIdentity(Name);
        }

        public void MarkAsFailedFromFile(Exception exception)
        {
            switch (LoadedFromFile)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as failed from file, it's already loaded.");
                case AssemblyLoadStatus.Failed:
                    throw new InvalidOperationException("Cannot mark assembly as failed from file, previous attempt failed.");
            }

            switch (LoadedFromName)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as failed from file, it's already loaded from name.");
            }

            LoadedFromFile = AssemblyLoadStatus.Failed;
            LoadedFromFileError = exception;
        }

        public void MarkAsLoadedFromName(Assembly assembly)
        {
            switch (LoadedFromName)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as loaded from name, it's already loaded.");
                case AssemblyLoadStatus.Failed:
                    throw new InvalidOperationException("Cannot mark assembly as loaded from name, previous attempt failed.");
            }

            switch (LoadedFromFile)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as loaded from name, it's already loaded from file.");
            }

            LoadedFromName = AssemblyLoadStatus.Loaded;
            Assembly = assembly;
            File = new FileInfo(assembly.Location);
        }

        public void MarkAsFailedFromName(Exception exception)
        {
            switch (LoadedFromName)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as failed from name, it's already loaded.");
                case AssemblyLoadStatus.Failed:
                    throw new InvalidOperationException("Cannot mark assembly as failed from name, previous attempt failed.");
            }

            switch (LoadedFromFile)
            {
                case AssemblyLoadStatus.Loaded:
                    throw new InvalidOperationException("Cannot mark assembly as failed from name, it's already loaded from file.");
            }

            LoadedFromName = AssemblyLoadStatus.Failed;
            LoadedFromNameError = exception;
        }
    }
}