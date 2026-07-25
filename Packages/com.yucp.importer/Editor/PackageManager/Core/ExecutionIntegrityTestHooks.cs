#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    internal static class VerificationIntentServiceTestHooks
    {
        internal static Action<string> OpenUrlHandler;

        internal static void Reset()
        {
            OpenUrlHandler = null;
        }
    }

}
#endif
