using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// MonoBehaviour component applied to prefab variants for model revision management.
    /// Handles variant-specific overrides and references to the base configuration.
    /// </summary>
    [BetaWarning("This component is in BETA and may not work as intended. Model revision variants are experimental and may require manual configuration.")]
    [AddComponentMenu("YUCP/Model Revision Variant")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")]
    [DisallowMultipleComponent]
    public class ModelRevisionVariant : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Header("Base Configuration")]
        [Tooltip("Reference to the ModelRevisionBase ScriptableObject")]
        public ModelRevisionBase revisionBase;
        
        [Header("Variant Overrides")]
        [Tooltip("Components that are manually added to this variant (overrides)")]
        public List<ComponentOverride> componentOverrides = new List<ComponentOverride>();
        
        [Tooltip("Blendshape mappings specific to this variant")]
        public List<BlendshapeMapping> variantBlendshapeMappings = new List<BlendshapeMapping>();
        
        [Tooltip("Component mappings specific to this variant")]
        public List<ComponentMapping> variantComponentMappings = new List<ComponentMapping>();
        
        [Header("Transfer Settings")]
        [Tooltip("Whether this variant should receive transfers")]
        public bool receiveTransfers = true;
        
        [Tooltip("Whether this variant should send transfers to others")]
        public bool sendTransfers = false;
        
        [Tooltip("Last sync timestamp")]
        public string lastSyncTimestamp;
        
        [Header("Debug Info")]
        [Tooltip("Cached bone paths for build-time processing")]
        public List<CachedBonePath> cachedBonePaths = new List<CachedBonePath>();
        
        [Tooltip("Status of this variant")]
        public VariantStatus status = VariantStatus.Unknown;
        
        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;
        
        private void Awake()
        {
            // Runtime validation
            if (revisionBase == null)
            {
                Debug.LogError($"[ModelRevisionVariant] No revision base assigned on '{name}'", this);
                return;
            }
            
            if (!revisionBase.registeredVariants.Contains(gameObject))
            {
                Debug.LogWarning($"[ModelRevisionVariant] Variant '{name}' not registered in base '{revisionBase.name}'", this);
            }
        }
        
        /// <summary>
        /// Add a component override to this variant
        /// </summary>
        public void AddComponentOverride(Component component, string bonePath)
        {
            if (component == null) return;
            
            var overrideData = new ComponentOverride
            {
                componentType = component.GetType().Name,
                componentInstance = component,
                bonePath = bonePath,
                isManualOverride = true
            };
            
            componentOverrides.Add(overrideData);
            Debug.Log($"[ModelRevisionVariant] Added override: {component.GetType().Name} on '{name}'");
        }
        
        /// <summary>
        /// Remove a component override from this variant
        /// </summary>
        public void RemoveComponentOverride(Component component)
        {
            if (component == null) return;
            
            componentOverrides.RemoveAll(o => o.componentInstance == component);
            Debug.Log($"[ModelRevisionVariant] Removed override: {component.GetType().Name} from '{name}'");
        }
        
        /// <summary>
        /// Check if a component is an override (manually added)
        /// </summary>
        public bool IsComponentOverride(Component component)
        {
            if (component == null) return false;
            return componentOverrides.Exists(o => o.componentInstance == component);
        }
        
        /// <summary>
        /// Get variant-specific blendshape mapping
        /// </summary>
        public BlendshapeMapping GetVariantBlendshapeMapping(string sourceName)
        {
            return variantBlendshapeMappings.Find(m => m.sourceName == sourceName);
        }
        
        /// <summary>
        /// Set variant-specific blendshape mapping
        /// </summary>
        public void SetVariantBlendshapeMapping(string sourceName, string targetName, float confidence = 1f)
        {
            var existing = GetVariantBlendshapeMapping(sourceName);
            if (existing != null)
            {
                existing.targetName = targetName;
                existing.confidence = confidence;
                existing.isManualOverride = true;
                existing.status = MappingStatus.Mapped;
            }
            else
            {
                variantBlendshapeMappings.Add(new BlendshapeMapping
                {
                    sourceName = sourceName,
                    targetName = targetName,
                    confidence = confidence,
                    isManualOverride = true,
                    status = MappingStatus.Mapped
                });
            }
        }
        
        /// <summary>
        /// Get variant-specific component mapping
        /// </summary>
        public ComponentMapping GetVariantComponentMapping(string componentType)
        {
            return variantComponentMappings.Find(m => m.componentType == componentType);
        }
        
        /// <summary>
        /// Set variant-specific component mapping
        /// </summary>
        public void SetVariantComponentMapping(string componentType, string sourceBonePath, string targetBonePath, bool enabled = true)
        {
            var existing = GetVariantComponentMapping(componentType);
            if (existing != null)
            {
                existing.sourceBonePath = sourceBonePath;
                existing.targetBonePath = targetBonePath;
                existing.enabled = enabled;
            }
            else
            {
                variantComponentMappings.Add(new ComponentMapping
                {
                    componentType = componentType,
                    sourceBonePath = sourceBonePath,
                    targetBonePath = targetBonePath,
                    enabled = enabled
                });
            }
        }
        
        /// <summary>
        /// Cache bone path for build-time processing
        /// </summary>
        public void CacheBonePath(string boneName, string bonePath)
        {
            var existing = cachedBonePaths.Find(c => c.boneName == boneName);
            if (existing != null)
            {
                existing.bonePath = bonePath;
            }
            else
            {
                cachedBonePaths.Add(new CachedBonePath
                {
                    boneName = boneName,
                    bonePath = bonePath
                });
            }
        }
        
        /// <summary>
        /// Get cached bone path
        /// </summary>
        public string GetCachedBonePath(string boneName)
        {
            var cached = cachedBonePaths.Find(c => c.boneName == boneName);
            return cached?.bonePath;
        }
        
        /// <summary>
        /// Update sync timestamp
        /// </summary>
        public void UpdateSyncTimestamp()
        {
            lastSyncTimestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            status = VariantStatus.Synced;
        }
        
        /// <summary>
        /// Mark variant as having overrides
        /// </summary>
        public void MarkAsOverridden()
        {
            status = VariantStatus.HasOverrides;
        }
        
        /// <summary>
        /// Mark variant as having conflicts
        /// </summary>
        public void MarkAsConflicted()
        {
            status = VariantStatus.HasConflicts;
        }
        
        /// <summary>
        /// Clear all variant-specific data
        /// </summary>
        public void ClearVariantData()
        {
            componentOverrides.Clear();
            variantBlendshapeMappings.Clear();
            variantComponentMappings.Clear();
            cachedBonePaths.Clear();
            status = VariantStatus.Unknown;
            lastSyncTimestamp = "";
            
            Debug.Log($"[ModelRevisionVariant] Cleared variant data for: {name}");
        }
        
        /// <summary>
        /// Validate this variant's configuration
        /// </summary>
        public List<string> ValidateVariant()
        {
            var issues = new List<string>();
            
            if (revisionBase == null)
            {
                issues.Add("No revision base assigned");
            }
            else if (!revisionBase.registeredVariants.Contains(gameObject))
            {
                issues.Add("Not registered in revision base");
            }
            
            // Check for invalid component overrides
            for (int i = componentOverrides.Count - 1; i >= 0; i--)
            {
                var overrideData = componentOverrides[i];
                if (overrideData.componentInstance == null)
                {
                    issues.Add($"Component override {i} has null component instance");
                    componentOverrides.RemoveAt(i);
                }
            }
            
            return issues;
        }
    }
    
    [Serializable]
    public class ComponentOverride
    {
        [Tooltip("Component type name")]
        public string componentType;
        
        [Tooltip("Component instance (runtime only)")]
        public Component componentInstance;
        
        [Tooltip("Bone path this component is attached to")]
        public string bonePath;
        
        [Tooltip("Whether this is a manual override")]
        public bool isManualOverride = true;
        
        [Tooltip("Override timestamp")]
        public string timestamp;
    }
    
    [Serializable]
    public class CachedBonePath
    {
        [Tooltip("Bone name")]
        public string boneName;
        
        [Tooltip("Cached bone path")]
        public string bonePath;
    }
    
    public enum VariantStatus
    {
        Unknown,
        Synced,
        HasOverrides,
        HasConflicts,
        OutOfSync
    }
}
