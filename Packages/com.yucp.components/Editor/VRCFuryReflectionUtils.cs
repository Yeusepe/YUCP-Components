using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace YUCP.Components.Editor
{
    internal static class VRCFuryReflectionUtils
    {
        private static readonly string[] EditorCommonAssemblyNames = { "VRCFury-Editor-Common", "VRCFury-Editor" };
        private static readonly string[] EditorAvatarAssemblyNames = { "VRCFury-Editor-Avatars", "VRCFury-Editor" };

        public static Assembly LoadEditorCommonAssembly()
        {
            return LoadAssembly(EditorCommonAssemblyNames);
        }

        public static Assembly LoadEditorAvatarAssembly()
        {
            return LoadAssembly(EditorAvatarAssemblyNames);
        }

        public static Type FindEditorCommonType(string fullName)
        {
            return FindType(fullName, EditorCommonAssemblyNames);
        }

        public static Type FindEditorAvatarType(string fullName)
        {
            return FindType(fullName, EditorAvatarAssemblyNames);
        }

        private static Type FindType(string fullName, string[] preferredAssemblyNames)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            var preferredAssembly = LoadAssembly(preferredAssemblyNames);
            return preferredAssembly?.GetType(fullName, false);
        }

        private static Assembly LoadAssembly(string[] assemblyNames)
        {
            foreach (var assemblyName in assemblyNames)
            {
                var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName);

                if (loadedAssembly != null)
                {
                    return loadedAssembly;
                }
            }

            foreach (var assemblyName in assemblyNames)
            {
                try
                {
                    return Assembly.Load(assemblyName);
                }
                catch (FileNotFoundException)
                {
                }
                catch (FileLoadException)
                {
                }
                catch (BadImageFormatException)
                {
                }
            }

            return null;
        }
    }
}
