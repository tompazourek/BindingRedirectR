namespace BindingRedirectR
{
    public static class SampleInputs
    {
        public static readonly InputParameters Foo = new InputParameters
        (
            assemblySources: new[]
            {
                @"C:\some-path\Some.Assembly.dll",
                @"C:\some-path\Some.Other.Assembly.dll",
            },
            mainAssemblySource: @"Some.Other.Assembly, Version=0.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            manualReferences: new[]
            {
                (@"Some.Other.Assembly, Version=0.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", @"Some.Baz.Assembly, Version=0.0.0.0, Culture=neutral, PublicKeyToken=a11a5c561934e000"),
            });
    }
}