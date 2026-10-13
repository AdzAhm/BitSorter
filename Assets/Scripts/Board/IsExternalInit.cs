namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// What C# 9's records and init-only setters need from the runtime, which netstandard2.1 -- the
    /// profile Unity builds this layer against -- predates. The compiler only looks for the name.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
