using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEditor;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Caches vertex detection results to disk. Reduces build time when mesh and settings haven't changed.
    /// Cache invalidates automatically using hash of mesh data, transforms, and detection parameters.
    /// </summary>
    public static class DetectionCache
    {
        private static Dictionary<string, CachedDetectionResult> _cache = new Dictionary<string, CachedDetectionResult>();
        private static readonly string CacheDirectory = "Library/YUCPCache";
        private static readonly string CacheFileName = "BodyHiderCache.json";
        
        [Serializable]
        private class CachedDetectionResult
        {
            public string hash;
            public bool[] hiddenVertices;
            public int hiddenCount;
            public long timestamp;
        }
        
        [Serializable]
        private class CacheData
        {
            public List<CachedDetectionResult> results = new List<CachedDetectionResult>();
        }

        static DetectionCache()
        {
            LoadCache();
        }

        public static bool TryGetCachedResult(
            AutoBodyHiderData data,
            Mesh bodyMesh,
            Mesh clothingMesh,
            out bool[] hiddenVertices,
            out int hiddenCount)
        {
            hiddenVertices = null;
            hiddenCount = 0;

            if (data == null || bodyMesh == null)
                return false;

            // Get all clothing meshes
            var clothingMeshes = data.GetClothingMeshes();
            Mesh[] clothingMeshArray = new Mesh[clothingMeshes.Length];
            for (int i = 0; i < clothingMeshes.Length; i++)
            {
                clothingMeshArray[i] = clothingMeshes[i].sharedMesh;
            }

            // Generate hash for current state
            string hash = GenerateHash(data, bodyMesh, clothingMeshArray);
            
            // Check cache
            if (_cache.TryGetValue(hash, out CachedDetectionResult cached))
            {
                if (data.debugMode)
                {
                    Debug.Log($"[DetectionCache] Cache hit! Reusing cached result with {cached.hiddenCount} hidden vertices.");
                }
                
                hiddenVertices = cached.hiddenVertices;
                hiddenCount = cached.hiddenCount;
                
                cached.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                return true;
            }
            
            if (data.debugMode)
            {
                Debug.Log($"[DetectionCache] Cache miss. Will compute and cache result.");
            }
            
            return false;
        }

        public static void CacheResult(
            AutoBodyHiderData data,
            Mesh bodyMesh,
            Mesh clothingMesh,
            bool[] hiddenVertices,
            int hiddenCount)
        {
            if (data == null || bodyMesh == null || hiddenVertices == null)
                return;

            // Get all clothing meshes
            var clothingMeshes = data.GetClothingMeshes();
            Mesh[] clothingMeshArray = new Mesh[clothingMeshes.Length];
            for (int i = 0; i < clothingMeshes.Length; i++)
            {
                clothingMeshArray[i] = clothingMeshes[i].sharedMesh;
            }

            string hash = GenerateHash(data, bodyMesh, clothingMeshArray);
            
            var cached = new CachedDetectionResult
            {
                hash = hash,
                hiddenVertices = hiddenVertices,
                hiddenCount = hiddenCount,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            _cache[hash] = cached;
            
            if (data.debugMode)
            {
                Debug.Log($"[DetectionCache] Cached result with hash: {hash.Substring(0, 8)}...");
            }
            
            SaveCache();
        }

        private static string GenerateHash(
            AutoBodyHiderData data,
            Mesh bodyMesh,
            Mesh[] clothingMeshes)
        {
            using (var sha256 = SHA256.Create())
            {
                var sb = new StringBuilder();
                
                sb.Append(GetMeshHash(bodyMesh));
                
                // Hash all clothing meshes
                if (clothingMeshes != null && clothingMeshes.Length > 0)
                {
                    sb.Append($"meshes_{clothingMeshes.Length}_");
                    foreach (var mesh in clothingMeshes)
                    {
                        if (mesh != null)
                {
                            sb.Append(GetMeshHash(mesh));
                        }
                    }
                }
                
                sb.Append(data.detectionMethod.ToString());
                sb.Append(data.safetyMargin.ToString("F4"));
                sb.Append(data.proximityThreshold.ToString("F4"));
                sb.Append(data.raycastDistance.ToString("F4"));
                sb.Append(data.hybridExpansionFactor.ToString("F4"));
                sb.Append(data.smartRayDirections);
                sb.Append(data.smartOcclusionThreshold.ToString("F4"));
                sb.Append(data.smartUseNormals);
                sb.Append(data.smartRequireBidirectional);
                sb.Append(data.mirrorSymmetry);
                sb.Append(data.useBoneFiltering);
                
                if (data.detectionMethod == DetectionMethod.Manual && data.manualMask != null)
                {
                    sb.Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data.manualMask)));
                    sb.Append(data.manualMaskThreshold.ToString("F4"));
                }
                
                if (data.targetBodyMesh != null)
                {
                    var t = data.targetBodyMesh.transform;
                    sb.Append(t.position.ToString());
                    sb.Append(t.rotation.ToString());
                    sb.Append(t.lossyScale.ToString());
                }
                
                // Hash transforms of all clothing meshes
                var clothingMeshRenderers = data.GetClothingMeshes();
                if (clothingMeshRenderers != null && clothingMeshRenderers.Length > 0)
                {
                    foreach (var renderer in clothingMeshRenderers)
                    {
                        if (renderer != null)
                {
                            var t = renderer.transform;
                    sb.Append(t.position.ToString());
                    sb.Append(t.rotation.ToString());
                    sb.Append(t.lossyScale.ToString());
                        }
                    }
                }
                
                byte[] inputBytes = Encoding.UTF8.GetBytes(sb.ToString());
                byte[] hashBytes = sha256.ComputeHash(inputBytes);
                
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        private static string GetMeshHash(Mesh mesh)
        {
            if (mesh == null)
                return "null";
            
            string assetPath = AssetDatabase.GetAssetPath(mesh);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrEmpty(guid))
                    return guid;
            }
            
            return $"{mesh.GetInstanceID()}_{mesh.vertexCount}_{mesh.triangles.Length}_{mesh.bounds.GetHashCode()}";
        }

        public static void ClearCache()
        {
            _cache.Clear();
            
            string fullPath = Path.Combine(Application.dataPath, "..", CacheDirectory, CacheFileName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            
            Debug.Log("[DetectionCache] Cache cleared.");
        }

        public static void ClearOldEntries()
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long weekInSeconds = 7 * 24 * 60 * 60;
            
            var toRemove = new List<string>();
            
            foreach (var kvp in _cache)
            {
                if (currentTime - kvp.Value.timestamp > weekInSeconds)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in toRemove)
            {
                _cache.Remove(key);
            }
            
            if (toRemove.Count > 0)
            {
                Debug.Log($"[DetectionCache] Removed {toRemove.Count} old cache entries.");
                SaveCache();
            }
        }

        private static void SaveCache()
        {
            try
            {
                string directoryPath = Path.Combine(Application.dataPath, "..", CacheDirectory);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                
                var cacheData = new CacheData();
                foreach (var kvp in _cache)
                {
                    cacheData.results.Add(kvp.Value);
                }
                
                string json = JsonUtility.ToJson(cacheData, true);
                string fullPath = Path.Combine(directoryPath, CacheFileName);
                File.WriteAllText(fullPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DetectionCache] Failed to save cache: {ex.Message}");
            }
        }

        private static void LoadCache()
        {
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", CacheDirectory, CacheFileName);
                if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    var cacheData = JsonUtility.FromJson<CacheData>(json);
                    
                    _cache.Clear();
                    foreach (var result in cacheData.results)
                    {
                        _cache[result.hash] = result;
                    }
                    
                    Debug.Log($"[DetectionCache] Loaded {_cache.Count} cached results.");
                    
                    ClearOldEntries();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DetectionCache] Failed to load cache: {ex.Message}");
                _cache.Clear();
            }
        }
    }
}

