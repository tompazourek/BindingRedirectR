using System;
using System.Globalization;
using System.Reflection;

namespace BindingRedirectR
{
    internal static class AssemblyMLoader
    {
        public static AssemblyMInfo ReflectionOnlyLoadFrom(string path)
        {
            using (var tempAppDomain = new TempAppDomain())
            {
                var assembly = tempAppDomain.ReflectionOnlyLoadFrom(path);
                return assembly;
            }
        }

        public static AssemblyMInfo ReflectionOnlyLoad(AssemblyName name)
        {
            using (var tempAppDomain = new TempAppDomain())
            {
                var assembly = tempAppDomain.ReflectionOnlyLoad(name.FullName);
                return assembly;
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
            public AssemblyMInfo ReflectionOnlyLoadFrom(string path)
                => _loaderProxy.ReflectionOnlyLoadFrom(path);

            public AssemblyMInfo ReflectionOnlyLoad(string assemblyName)
                => _loaderProxy.ReflectionOnlyLoad(assemblyName);

            public void Dispose()
            {
                AppDomain.Unload(_appDomain);
            }
        }

        private class AssemblyLoaderProxy : MarshalByRefObject
        {
            // ReSharper disable once MemberHidesStaticFromOuterClass
            public AssemblyMInfo ReflectionOnlyLoadFrom(string path)
            {
                var assembly = Assembly.ReflectionOnlyLoadFrom(path);
                var result = new AssemblyMInfo(assembly);
                return result;
            }

            public AssemblyMInfo ReflectionOnlyLoad(string assemblyName)
            {
                var assembly = Assembly.ReflectionOnlyLoad(assemblyName);
                var result = new AssemblyMInfo(assembly);
                return result;
            }
        }
    }
}