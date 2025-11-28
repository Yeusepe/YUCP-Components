using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.UI.DesignSystem.IMGUI
{
    /// <summary>
    /// Multi-step wizard flow for guided configuration.
    /// Provides step-by-step setup with navigation controls.
    /// </summary>
    public class YUCPWizard
    {
        private readonly List<string> steps = new List<string>();
        private int currentStep = 0;
        private readonly string stateKey;

        public YUCPWizard(string stateKey = null)
        {
            this.stateKey = stateKey ?? $"YUCPWizard_{GetHashCode()}";
            currentStep = SessionState.GetInt(stateKey, 0);
        }

        public void AddStep(string stepName)
        {
            steps.Add(stepName);
        }

        public int DrawWizard(System.Action<int> drawStepContent)
        {
            DrawStepIndicator();
            EditorGUILayout.Space(8);
            
            if (currentStep >= 0 && currentStep < steps.Count)
            {
                drawStepContent?.Invoke(currentStep);
            }
            
            EditorGUILayout.Space(8);
            DrawNavigation();
            
            return currentStep;
        }

        private void DrawStepIndicator()
        {
            EditorGUILayout.BeginHorizontal();
            
            for (int i = 0; i < steps.Count; i++)
            {
                bool isActive = i == currentStep;
                bool isCompleted = i < currentStep;
                
                var style = GetStepStyle(isActive, isCompleted);
                var content = new GUIContent($"{i + 1}. {steps[i]}");
                
                if (GUILayout.Button(content, style))
                {
                    currentStep = i;
                    SessionState.SetInt(stateKey, currentStep);
                }
                
                if (i < steps.Count - 1)
                {
                    GUILayout.Label("→", EditorStyles.miniLabel, GUILayout.Width(20));
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNavigation()
        {
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = currentStep > 0;
            if (YUCPButton.Draw("Previous", YUCPButton.ButtonVariant.Secondary))
            {
                currentStep--;
                SessionState.SetInt(stateKey, currentStep);
            }
            
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            
            GUI.enabled = currentStep < steps.Count - 1;
            if (YUCPButton.Draw("Next", YUCPButton.ButtonVariant.Primary))
            {
                currentStep++;
                SessionState.SetInt(stateKey, currentStep);
            }
            
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
        }

        private GUIStyle GetStepStyle(bool isActive, bool isCompleted)
        {
            var style = new GUIStyle(EditorStyles.miniButton);
            
            if (isActive)
            {
                style.normal.background = CreateColorTexture(new Color(0.212f, 0.749f, 0.694f, 1f));
                style.normal.textColor = Color.white;
                style.fontStyle = FontStyle.Bold;
            }
            else if (isCompleted)
            {
                style.normal.textColor = new Color(0.212f, 0.749f, 0.694f, 1f);
            }
            else
            {
                style.normal.textColor = new Color(0.69f, 0.69f, 0.69f, 1f);
            }
            
            return style;
        }

        private Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        public int CurrentStep => currentStep;
        public int TotalSteps => steps.Count;
        public bool IsComplete => currentStep >= steps.Count - 1;
    }
}

