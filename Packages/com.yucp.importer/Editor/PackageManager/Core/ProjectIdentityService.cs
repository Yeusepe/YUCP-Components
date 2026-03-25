using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YUCP.Importer.Editor.PackageManager.Core
{
    public static class ProjectIdentityService
    {
        private const string SessionKey = "yucp.project.identity";

        private static string IdentityFilePath =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "ProjectSettings", "YUCPProjectIdentity.json");

        public static string GetOrCreateProjectId()
        {
            string cached = SessionState.GetString(SessionKey, null);
            if (!string.IsNullOrEmpty(cached))
                return cached;

            try
            {
                if (File.Exists(IdentityFilePath))
                {
                    var existing = JsonUtility.FromJson<ProjectIdentityFile>(File.ReadAllText(IdentityFilePath));
                    if (!string.IsNullOrEmpty(existing?.projectId))
                    {
                        SessionState.SetString(SessionKey, existing.projectId);
                        return existing.projectId;
                    }
                }

                string projectId = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(IdentityFilePath) ?? string.Empty);
                File.WriteAllText(IdentityFilePath, JsonUtility.ToJson(new ProjectIdentityFile { projectId = projectId }, true));
                SessionState.SetString(SessionKey, projectId);
                AssetDatabase.Refresh();
                return projectId;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[YUCP ProjectIdentity] Failed to initialize project identity: {ex.Message}");
                return null;
            }
        }

        [Serializable]
        private class ProjectIdentityFile
        {
            public string projectId;
        }
    }
}
