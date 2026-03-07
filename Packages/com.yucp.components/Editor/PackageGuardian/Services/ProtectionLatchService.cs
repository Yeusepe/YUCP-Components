using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace YUCP.Components.PackageGuardian.Editor.Services
{
    internal static class ProtectionLatchService
    {
        private const string StateSalt = "pg.latch.v1";

        [Serializable]
        private sealed class LatchEntry
        {
            public string k;
            public string r;
            public string t;
        }

        [Serializable]
        private sealed class LatchState
        {
            public List<LatchEntry> e = new List<LatchEntry>();
            public string h;
        }

        public static void Set(string key, string reason)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var state = LoadOrDefault();
            var item = state.e.FirstOrDefault(x => string.Equals(x.k, key, StringComparison.Ordinal));
            if (item == null)
            {
                item = new LatchEntry { k = key };
                state.e.Add(item);
            }

            item.r = reason ?? string.Empty;
            item.t = DateTime.UtcNow.ToString("O");
            Save(state);
        }

        public static void Clear(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var state = LoadOrDefault();
            state.e.RemoveAll(x => string.Equals(x.k, key, StringComparison.Ordinal));
            Save(state);
        }

        public static bool IsLocked(out string summary)
        {
            summary = string.Empty;

            bool tampered;
            LatchState state;
            if (!TryLoad(out state, out tampered))
            {
                if (tampered)
                {
                    summary = "state_tamper";
                    return true;
                }
                return false;
            }

            if (state == null || state.e == null || state.e.Count == 0)
                return false;

            summary = string.Join("; ", state.e
                .Where(x => !string.IsNullOrWhiteSpace(x.k))
                .OrderBy(x => x.k, StringComparer.Ordinal)
                .Select(x => $"{x.k}:{x.r}"));
            return true;
        }

        public static bool HasKey(string key, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            bool tampered;
            LatchState state;
            if (!TryLoad(out state, out tampered))
            {
                if (tampered)
                {
                    reason = "state_tamper";
                    return true;
                }
                return false;
            }

            if (state == null || state.e == null)
                return false;

            var item = state.e.FirstOrDefault(x => string.Equals(x.k, key, StringComparison.Ordinal));
            if (item == null)
                return false;

            reason = item.r ?? string.Empty;
            return true;
        }

        private static string GetPath()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "YUCP", "PackageGuardian"));
            return Path.Combine(root, "latch.dat");
        }

        private static LatchState LoadOrDefault()
        {
            bool tampered;
            LatchState state;
            if (TryLoad(out state, out tampered) && state != null)
                return state;
            if (tampered)
            {
                var forced = new LatchState();
                forced.e.Add(new LatchEntry
                {
                    k = "pg_state",
                    r = "tamper",
                    t = DateTime.UtcNow.ToString("O")
                });
                return forced;
            }
            return new LatchState();
        }

        private static bool TryLoad(out LatchState state, out bool tampered)
        {
            state = null;
            tampered = false;

            try
            {
                var path = GetPath();
                if (!File.Exists(path))
                    return false;

                var raw = File.ReadAllText(path, Encoding.UTF8);
                state = JsonUtility.FromJson<LatchState>(raw);
                if (state == null)
                {
                    tampered = true;
                    return false;
                }

                var expected = ComputeHash(state.e);
                if (!string.Equals(expected, state.h ?? string.Empty, StringComparison.Ordinal))
                {
                    tampered = true;
                    return false;
                }

                if (state.e == null)
                    state.e = new List<LatchEntry>();
                return true;
            }
            catch
            {
                tampered = true;
                return false;
            }
        }

        private static void Save(LatchState state)
        {
            try
            {
                var entries = state?.e ?? new List<LatchEntry>();
                entries.RemoveAll(x => x == null || string.IsNullOrWhiteSpace(x.k));
                if (entries.Count == 0)
                {
                    var path = GetPath();
                    if (File.Exists(path))
                        File.Delete(path);
                    return;
                }

                var normalized = new LatchState { e = entries };
                normalized.h = ComputeHash(normalized.e);

                var outputPath = GetPath();
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outputPath, JsonUtility.ToJson(normalized, false), Encoding.UTF8);
            }
            catch { }
        }

        private static string ComputeHash(IEnumerable<LatchEntry> entries)
        {
            string proj = string.Empty;
            try { proj = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').ToLowerInvariant(); } catch { }
            string device = string.Empty;
            try { device = SystemInfo.deviceUniqueIdentifier ?? string.Empty; } catch { }

            var sb = new StringBuilder();
            sb.Append(StateSalt).Append('|').Append(proj).Append('|').Append(device);

            foreach (var e in (entries ?? Array.Empty<LatchEntry>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.k))
                .OrderBy(x => x.k, StringComparer.Ordinal))
            {
                sb.Append('|').Append(e.k ?? string.Empty)
                  .Append('|').Append(e.r ?? string.Empty)
                  .Append('|').Append(e.t ?? string.Empty);
            }

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                for (var i = 0; i < bytes.Length; i++)
                    hex.Append(bytes[i].ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
