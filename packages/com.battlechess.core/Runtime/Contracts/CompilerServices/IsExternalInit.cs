// netstandard2.1 predates the init-only setter, but the C# 9 compiler only
// needs this marker type to exist. Declaring it ourselves lets us use
// `init` accessors in DTOs while staying on the Unity-compatible target.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
