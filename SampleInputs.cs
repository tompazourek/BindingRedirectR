namespace BindingRedirectR
{
    public static class SampleInputs
    {
        public static readonly InputParameters Foo = new InputParameters
        (
            assemblySources: new[]
            {
                @"some-path",
                @"some-other-path",
            },
            mainAssemblySource: @"some-path",
            manualReferences: new[]
            {
                ("some-path", "some-x-path"),
            });
    }
}