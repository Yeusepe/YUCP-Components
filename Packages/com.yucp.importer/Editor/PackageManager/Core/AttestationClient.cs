using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    /// <summary>
    /// Client side of the hardware-attested anti-ripper identity. Runs at protected unlock time:
    /// requests a challenge from the closed coupling-service, invokes the installed native collector
    /// (which reads the HWID constellation + TPM and seals everything to the server attestation key),
    /// submits the sealed blob, and reports whether this buyer resolves to a blocked identity.
    ///
    /// This managed code is deliberately thin: it never sees raw identifiers (the native binary seals
    /// them) and it makes no trust decision of its own. The verdict comes from the closed service.
    /// </summary>
    internal static class AttestationClient
    {
        private const int RequestTimeoutSeconds = 30;
        private const int SealBufferCapacity = 64 * 1024;

        // Pinned P-256 server attestation public key (SPKI, base64). In the shipping runtime this is
        // compiled into the obfuscated native binary; the managed side passes it through so the native
        // collector seals to it. Resolved from settings so it is not hard-coded in open source.
        private static string ResolveServerAttestationKey()
        {
            return TrustedAttestationKey.SpkiBase64;
        }

        internal struct AttestationOutcome
        {
            public bool completed;
            public bool blocked;
            public string error;
        }

        /// <summary>
        /// Run the full attestation. Returns completed=false with an error when the flow could not run
        /// (caller decides whether that is fatal); blocked=true means the unlock must be refused.
        /// </summary>
        internal static AttestationOutcome Run(string licenseSubject, string authUserId)
        {
            var outcome = new AttestationOutcome();

            string serviceUrl = CouplingServiceResolver.GetCouplingServiceUrl();
            if (string.IsNullOrEmpty(serviceUrl))
            {
                outcome.error = "Could not resolve the attestation service URL.";
                return outcome;
            }

            string serverKey = ResolveServerAttestationKey();
            if (string.IsNullOrEmpty(serverKey))
            {
                outcome.error = "Attestation server key is not configured.";
                return outcome;
            }

            string correlationId = Guid.NewGuid().ToString("N");

            if (!TryRequestChallenge(serviceUrl, correlationId, out string nonce, out outcome.error))
            {
                return outcome;
            }

            // All identity/HWID/VRChat collection happens inside the obfuscated native collector so
            // managed code never sees plaintext identifiers. C# only forwards the managed-side claims
            // it legitimately holds (the license subject and auth user id from the verified token).
            string extrasJson = BuildExtrasJson(licenseSubject, authUserId);

            if (!NativeAttestationCollector.TryCollect(serverKey, nonce, correlationId, extrasJson,
                    out string sealedJson, out outcome.error))
            {
                return outcome;
            }

            if (!TrySubmit(serviceUrl, sealedJson, out bool blocked, out outcome.error))
            {
                return outcome;
            }

            outcome.completed = true;
            outcome.blocked = blocked;
            return outcome;
        }

        private static bool TryRequestChallenge(string serviceUrl, string correlationId, out string nonce, out string error)
        {
            nonce = null;
            error = null;
            string body = "{\"correlationId\":\"" + correlationId + "\"}";
            using var request = new UnityWebRequest($"{serviceUrl.TrimEnd('/')}/v1/attestation/challenge", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            var op = request.SendWebRequest();
            while (!op.isDone) Thread.Sleep(20);

            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                error = $"Attestation challenge failed (HTTP {request.responseCode}).";
                return false;
            }
            var parsed = JsonUtility.FromJson<ChallengeResponse>(request.downloadHandler.text);
            if (parsed == null || string.IsNullOrEmpty(parsed.nonce))
            {
                error = "Attestation challenge returned no nonce.";
                return false;
            }
            nonce = parsed.nonce;
            return true;
        }

        private static bool TrySubmit(string serviceUrl, string sealedJson, out bool blocked, out string error)
        {
            blocked = false;
            error = null;
            using var request = new UnityWebRequest($"{serviceUrl.TrimEnd('/')}/v1/attestation/submit", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(sealedJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            var op = request.SendWebRequest();
            while (!op.isDone) Thread.Sleep(20);

            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                error = $"Attestation submit failed (HTTP {request.responseCode}).";
                return false;
            }
            var parsed = JsonUtility.FromJson<SubmitResponse>(request.downloadHandler.text);
            if (parsed == null || !parsed.success)
            {
                error = "Attestation submit was rejected.";
                return false;
            }
            blocked = parsed.blocked;
            return true;
        }

        private static string BuildExtrasJson(string licenseSubject, string authUserId)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            AppendField(sb, ref first, "licenseSubject", licenseSubject);
            AppendField(sb, ref first, "authUserId", authUserId);
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendField(StringBuilder sb, ref bool first, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!first) sb.Append(",");
            first = false;
            sb.Append("\"").Append(key).Append("\":\"").Append(JsonEscape(value)).Append("\"");
        }

        private static string JsonEscape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        [Serializable]
        private class ChallengeResponse
        {
            public string nonce;
            public long expiresAt;
        }

        [Serializable]
        private class SubmitResponse
        {
            public bool success;
            public bool blocked;
        }
    }

    /// <summary>
    /// Invokes the installed native collector export (YucpCollectAttestation) by dynamically loading
    /// the active runtime DLL. The native binary performs all hardware reads, the TPM attestation, and
    /// the channel sealing; this managed seam only marshals strings in and the sealed JSON out.
    /// </summary>
    internal static class NativeAttestationCollector
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate int CollectDelegate(
            string serverPubSpkiB64, string nonce, string correlationId, string extrasJson,
            StringBuilder outBuf, int outCap, out int outNeeded);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        internal static bool TryCollect(string serverKey, string nonce, string correlationId,
            string extrasJson, out string sealedJson, out string error)
        {
            sealedJson = null;
            error = null;

            string dllPath = ResolveActiveRuntimeDllPath();
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                error = "The native attestation runtime is not installed.";
                return false;
            }

            IntPtr module = LoadLibrary(dllPath);
            if (module == IntPtr.Zero)
            {
                error = "Could not load the native attestation runtime.";
                return false;
            }

            try
            {
                IntPtr proc = GetProcAddress(module, "YucpCollectAttestation");
                if (proc == IntPtr.Zero)
                {
                    error = "The native attestation entry point is missing.";
                    return false;
                }

                var collect = Marshal.GetDelegateForFunctionPointer<CollectDelegate>(proc);
                var buffer = new StringBuilder(AttestationBufferCapacity);
                int rc = collect(serverKey, nonce, correlationId, extrasJson ?? "{}", buffer,
                    buffer.Capacity, out int needed);
                if (rc == -2)
                {
                    buffer = new StringBuilder(Math.Max(needed, AttestationBufferCapacity));
                    rc = collect(serverKey, nonce, correlationId, extrasJson ?? "{}", buffer,
                        buffer.Capacity, out needed);
                }
                if (rc != 0)
                {
                    error = $"The native attestation collector failed (code {rc}).";
                    return false;
                }
                sealedJson = buffer.ToString();
                return !string.IsNullOrEmpty(sealedJson);
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        private const int AttestationBufferCapacity = 64 * 1024;

        private static string ResolveActiveRuntimeDllPath()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string statePath = Path.Combine(localAppData, "Programs", "YUCP", "CouplingRuntime", "state", "active.json");
                if (!File.Exists(statePath)) return null;
                var state = JsonUtility.FromJson<ActiveState>(File.ReadAllText(statePath));
                return state?.activeDllPath;
            }
            catch
            {
                return null;
            }
        }

        [Serializable]
        private class ActiveState
        {
            public string activeBuildId;
            public string activeVersion;
            public string activePackageDir;
            public string activeDllPath;
        }
    }
}
