// Polyfill for C# 9.0 record support in Unity
// This allows using 'record' and 'init' keywords in older C# versions

namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;
    
    /// <summary>
    /// Reserved for compiler use. Enables record types and init-only setters.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit
    {
    }
}

