# BindingRedirectR

A tool to analyse assembly dependencies, possibly generate binding redirects.

**IMPORTANT! It's only a proof of concept (pre-alpha) at this stage.**

## Run

To run, execute in command line:

```
BindingRedirectR.exe input.json
```

The generated report will be in: `input.json.log` file in the same folder.

The `input.json` file looks something like this:

```
{
  "baseDirectory": null,
  "mainAssembly": "BindingRedirectR.exe",
  "assemblies": [
    "Serilog*.dll",
    "Newtonsoft.Json.dll",
    "Microsoft.Extensions.FileSystemGlobbing.dll"
  ],
  "additionalDependencies": [
    {
      "dependant": "BindingRedirectR.exe",
      "dependencies": [ "System.Diagnostics.Tracing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a" ]
    }
  ]
}
```

The assembly strings can be either file paths, file globbing patterns, or fully qualified assembly names.