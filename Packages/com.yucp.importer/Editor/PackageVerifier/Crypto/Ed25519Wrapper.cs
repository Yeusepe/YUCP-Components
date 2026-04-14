using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using YUCP.Importer.Editor.PackageManager.Core;

namespace YUCP.Importer.Editor.PackageVerifier.Crypto
{
    /// <summary>
    /// Ed25519 wrapper for verification in Unity using Chaos.NaCl.Standard
    /// </summary>
    public static class Ed25519Wrapper
    {
        private const int PublicKeySize = 32;
        private const int SignatureSize = 64;
        private const string ChaosNaClAssemblyName = "Chaos.NaCl";
        private const string ChaosNaClTypeName = "Chaos.NaCl.Ed25519";
        private const string ExpectedChaosNaClSha256 = "f442b14191f55536e7b72ec83a056f5ed1c55aaa2f44a0f95f00a4a24a286311";

        private static bool _useChaosNaCl = false;
        private static bool _initialized = false;
        private static Assembly _chaosNaClAssembly;
        private static Type _ed25519Type;

        static Ed25519Wrapper()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                if (!TryResolveEd25519Type(out _ed25519Type, out string error))
                {
                    Debug.LogError($"[Ed25519Wrapper] {error}");
                    return;
                }

                _useChaosNaCl = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Ed25519Wrapper] Error detecting Chaos.NaCl: {ex.Message}");
            }

            if (!_useChaosNaCl)
            {
                UnityEngine.Debug.LogError("[Ed25519Wrapper] Chaos.NaCl.Standard not found. Ed25519 verification requires Chaos.NaCl.dll to be installed in the Plugins folder.");
            }
        }

        /// <summary>
        /// Verify signature with public key
        /// </summary>
        public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (signature == null || signature.Length != SignatureSize)
                throw new ArgumentException("Invalid signature (must be 64 bytes)", nameof(signature));
            if (publicKey == null || publicKey.Length != PublicKeySize)
                throw new ArgumentException("Invalid public key (must be 32 bytes)", nameof(publicKey));

            if (_useChaosNaCl)
            {
                return VerifyChaosNaCl(data, signature, publicKey);
            }
            else
            {
                throw new InvalidOperationException(
                    "Ed25519 verification requires Chaos.NaCl.Standard library. " +
                    "Please install Chaos.NaCl.dll in your project's Plugins folder. " +
                    "Download from: https://www.nuget.org/packages/Chaos.NaCl.Standard/1.0.0 " +
                    "and extract lib/netstandard2.0/Chaos.NaCl.dll to Assets/Plugins/");
            }
        }

        #region Chaos.NaCl Implementation

        private static bool VerifyChaosNaCl(byte[] data, byte[] signature, byte[] publicKey)
        {
            try
            {
                if (_ed25519Type == null)
                {
                    return false;
                }

                var verifyMethod = _ed25519Type.GetMethod("Verify", new[] { typeof(byte[]), typeof(byte[]), typeof(byte[]) });
                if (verifyMethod == null)
                {
                    return false;
                }

                bool isValid = (bool)verifyMethod.Invoke(null, new object[] { signature, data, publicKey });
                return isValid;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Ed25519Wrapper] Verification error: {ex.Message}");
                return false;
            }
        }

        private static bool TryResolveEd25519Type(out Type ed25519Type, out string error)
        {
            ed25519Type = null;
            error = null;

            string chaosNaClPath = ResolveChaosNaClAssemblyPath();
            if (string.IsNullOrWhiteSpace(chaosNaClPath))
            {
                error = "Chaos.NaCl.Standard not found. Ed25519 verification requires Plugins\\Chaos.NaCl.dll.";
                return false;
            }

            if (!TryLoadVerifiedChaosNaClAssembly(chaosNaClPath, ExpectedChaosNaClSha256, out _chaosNaClAssembly, out error))
            {
                return false;
            }

            ed25519Type = _chaosNaClAssembly?.GetType(ChaosNaClTypeName, throwOnError: false);
            if (ed25519Type == null)
            {
                error = "The verified Chaos.NaCl assembly does not expose Chaos.NaCl.Ed25519.";
                return false;
            }

            return true;
        }

        private static string ResolveChaosNaClAssemblyPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            string candidatePath = Path.Combine(projectRoot, "Plugins", "Chaos.NaCl.dll");
            return File.Exists(candidatePath)
                ? Path.GetFullPath(candidatePath)
                : null;
        }

        private static bool TryLoadVerifiedChaosNaClAssembly(
            string assemblyPath,
            string expectedSha256,
            out Assembly assembly,
            out string error)
        {
            assembly = null;
            error = null;

            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                error = "The pinned Chaos.NaCl assembly is missing from the project Plugins folder.";
                return false;
            }

            if (!RuntimeExecutionSecurityUtility.TryOpenLockedRead(assemblyPath, out var stream, out byte[] bytes, out error))
            {
                return false;
            }

            using (stream)
            {
                string actualSha256 = RuntimeExecutionSecurityUtility.ComputeSha256Hex(bytes);
                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The pinned Chaos.NaCl assembly failed integrity verification.";
                    return false;
                }

                assembly = FindLoadedChaosNaClAssembly(assemblyPath) ?? Assembly.Load(bytes);
            }

            AssemblyName assemblyName;
            try
            {
                assemblyName = assembly.GetName();
            }
            catch (Exception ex)
            {
                error = $"The pinned Chaos.NaCl assembly could not be inspected: {ex.Message}";
                return false;
            }

            if (!string.Equals(assemblyName.Name, ChaosNaClAssemblyName, StringComparison.Ordinal))
            {
                error = $"The pinned Chaos.NaCl assembly exposed an unexpected identity: {assemblyName.FullName}";
                return false;
            }

            return true;
        }

        private static Assembly FindLoadedChaosNaClAssembly(string expectedPath)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }

                try
                {
                    AssemblyName name = assembly.GetName();
                    if (!string.Equals(name.Name, ChaosNaClAssemblyName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string assemblyLocation = assembly.Location;
                    if (!string.IsNullOrWhiteSpace(assemblyLocation) &&
                        string.Equals(Path.GetFullPath(assemblyLocation), expectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        #endregion

    }
}



