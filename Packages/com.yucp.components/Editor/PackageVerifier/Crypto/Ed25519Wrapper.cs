using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

namespace YUCP.Components.Editor.PackageVerifier.Crypto
{
    /// <summary>
    /// Ed25519 wrapper for verification in Unity using Chaos.NaCl.Standard
    /// </summary>
    public static class Ed25519Wrapper
    {
        private const int PublicKeySize = 32;
        private const int SignatureSize = 64;

        private static bool _useChaosNaCl = false;
        private static bool _initialized = false;

        static Ed25519Wrapper()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            // Try to detect Chaos.NaCl by checking loaded assemblies (similar to Harmony pattern)
            try
            {
                bool chaosNaClAvailable = false;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.FullName.StartsWith("Chaos.NaCl"))
                    {
                        chaosNaClAvailable = true;
                        break;
                    }
                }

                // If not found in loaded assemblies, try to load from Plugins folder
                if (!chaosNaClAvailable)
                {
                    // Get the root project folder (parent of Assets)
                    string projectRoot = Path.GetDirectoryName(Application.dataPath);
                    
                    string[] possiblePaths = new[]
                    {
                        // Root Plugins folder (where it actually is!)
                        Path.Combine(projectRoot, "Plugins", "Chaos.NaCl.dll"),
                        // Main project Plugins
                        Path.Combine(Application.dataPath, "Plugins", "Chaos.NaCl.dll"),
                        Path.Combine(Application.dataPath, "Plugins", "x86_64", "Chaos.NaCl.dll"),
                        Path.Combine(Application.dataPath, "Plugins", "x86", "Chaos.NaCl.dll"),
                    };

                    foreach (var dllPath in possiblePaths)
                    {
                        if (File.Exists(dllPath))
                        {
                            try
                            {
                                Assembly.LoadFrom(dllPath);
                                chaosNaClAvailable = true;
                                Debug.Log($"[Ed25519Wrapper] Loaded Chaos.NaCl from {dllPath}");
                                break;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[Ed25519Wrapper] Failed to load Chaos.NaCl from {dllPath}: {ex.Message}");
                            }
                        }
                    }
                }

                if (chaosNaClAvailable)
                {
                    var ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
                    if (ed25519Type != null)
                    {
                        _useChaosNaCl = true;
                        _initialized = true;
                        UnityEngine.Debug.Log("[Ed25519Wrapper] Using Chaos.NaCl library");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Ed25519Wrapper] Error detecting Chaos.NaCl: {ex.Message}");
            }

            UnityEngine.Debug.LogError("[Ed25519Wrapper] Chaos.NaCl.Standard not found. Ed25519 verification requires Chaos.NaCl.dll to be installed in the Plugins folder.");
            _initialized = true;
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
                // Get Ed25519 type via reflection
                var ed25519Type = Type.GetType("Chaos.NaCl.Ed25519, Chaos.NaCl");
                if (ed25519Type == null)
                {
                    return false;
                }

                var verifyMethod = ed25519Type.GetMethod("Verify", new[] { typeof(byte[]), typeof(byte[]), typeof(byte[]) });
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

        #endregion

    }
}


