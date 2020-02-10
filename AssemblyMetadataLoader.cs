using System;
using System.Globalization;
using System.Reflection;

namespace BindingRedirectR
{
    internal static class AssemblyMetadataLoader
    {
        public static AssemblyMetadata ReflectionOnlyLoadFrom(string path)
        {
            using (var tempAppDomain = new TempAppDomain())
            {
                var assemblyMetadata = tempAppDomain.ReflectionOnlyLoadFrom(path);
                return assemblyMetadata;
            }
        }

        public static AssemblyMetadata ReflectionOnlyLoad(AssemblyName name)
        {
            using (var tempAppDomain = new TempAppDomain())
            {
                var assemblyMetadata = tempAppDomain.ReflectionOnlyLoad(name.FullName);
                return assemblyMetadata;
            }
        }

        private class TempAppDomain : IDisposable
        {
            private readonly AppDomain _appDomain;
            private readonly AssemblyLoaderProxy _loaderProxy;

            public TempAppDomain()
            {
                var settings = new AppDomainSetup
                {
                    ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                };

                _appDomain = AppDomain.CreateDomain(Guid.NewGuid().ToString(), securityInfo: null, settings);

                var handle = Activator.CreateInstance
                (
                    _appDomain,
                    typeof(AssemblyLoaderProxy).Assembly.FullName,
                    // ReSharper disable once AssignNullToNotNullAttribute
                    typeof(AssemblyLoaderProxy).FullName,
                    ignoreCase: false,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    args: null,
                    CultureInfo.CurrentCulture,
                    activationAttributes: new object[0]
                );

                _loaderProxy = (AssemblyLoaderProxy)handle.Unwrap();
            }

            // ReSharper disable once MemberHidesStaticFromOuterClass
            public AssemblyMetadata ReflectionOnlyLoadFrom(string path)
                => _loaderProxy.ReflectionOnlyLoadFrom(path);

            public AssemblyMetadata ReflectionOnlyLoad(string assemblyName)
                => _loaderProxy.ReflectionOnlyLoad(assemblyName);

            public void Dispose()
            {
                AppDomain.Unload(_appDomain);
            }
        }

        private class AssemblyLoaderProxy : MarshalByRefObject
        {
            // ReSharper disable once MemberHidesStaticFromOuterClass
            public AssemblyMetadata ReflectionOnlyLoadFrom(string path)
            {
                var assembly = Assembly.ReflectionOnlyLoadFrom(path);
                var assemblyMetadata = new AssemblyMetadata(assembly);
                return assemblyMetadata;
            }

            public AssemblyMetadata ReflectionOnlyLoad(string assemblyName)
            {
                var assembly = Assembly.ReflectionOnlyLoad(assemblyName);
                var assemblyMetadata = new AssemblyMetadata(assembly);
                return assemblyMetadata;
            }
        }
    }
}