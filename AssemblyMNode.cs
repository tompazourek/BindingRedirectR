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
        public AssemblyMInfo Assembly { get; private set; }

        public AssemblyName Name { get; private set; }
        public AssemblyLoadStatus LoadedFromName { get; private set; }
        public Exception LoadedFromNameError { get; private set; }

        public FileInfo File { get; private set; }
        public AssemblyLoadStatus LoadedFromFile { get; private set; }
        public Exception LoadedFromFileError { get; private set; }

        public bool Loaded { get; private set; }

        public static AssemblyMNode CreateFromName(AssemblyName assemblyName)
        {
            if (assemblyName == null) throw new ArgumentNullException(nameof(assemblyName));
            Log.Debug("Creating node from name {AssemblyName}.", assemblyName);
            return new AssemblyMNode
            {
                Name = assemblyName,
                Identity = new AssemblySpecificIdentity(assemblyName),
            };
        }

        public static AssemblyMNode CreateFromFile(FileInfo file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            Log.Debug("Creating node from file {File}.", file.FullName);
            return new AssemblyMNode
            {
                File = file,
                // we don't have identity until we attempt to load
            };
        }

        public void MarkAsLoadedFromFile(AssemblyMInfo assembly)
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

            if (Loaded)
                throw new InvalidOperationException("Cannot mark assembly as loaded from file, it's already been loaded.");

            LoadedFromFile = AssemblyLoadStatus.Loaded;
            Assembly = assembly;
            Name = new AssemblyName(assembly.AssemblyName);
            Identity = new AssemblySpecificIdentity(Name);
            Loaded = true;
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

            if (Loaded)
                throw new InvalidOperationException("Cannot mark assembly as failed from file, it's already been loaded.");

            LoadedFromFile = AssemblyLoadStatus.Failed;
            LoadedFromFileError = exception;
        }

        public void MarkAsLoadedFromName(AssemblyMInfo assembly)
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

            if (Loaded)
                throw new InvalidOperationException("Cannot mark assembly as loaded from name, it's already been loaded.");

            LoadedFromName = AssemblyLoadStatus.Loaded;
            Assembly = assembly;
            File = new FileInfo(assembly.Location);
            Loaded = true;
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

            if (Loaded)
                throw new InvalidOperationException("Cannot mark assembly as failed from name, it's already been loaded.");

            LoadedFromName = AssemblyLoadStatus.Failed;
            LoadedFromNameError = exception;
        }

        public override string ToString()
        {
            if (!Loaded)
            {
                if (File != null)
                    return $"[not loaded] {File.FullName}";
                if (Name != null)
                    return $"[not loaded] {Name.FullName}";
                return "[not loaded] (unknown)"; // shouldn't happen
            }

            return Identity.ToString();
        }
    }
}