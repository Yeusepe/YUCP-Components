using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using YUCP.Components;

namespace YUCP.UI.DesignSystem.Utilities
{
    /// <summary>
    /// UI Toolkit component for displaying solver mode information card.
    /// </summary>
    public class YUCPSolverModeCard : VisualElement
    {
        private Label titleLabel;
        private Label descriptionLabel;
        private Label goodForLabel;
        
        public YUCPSolverModeCard()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.yucp.components/Editor/UI/DesignSystem/UIToolkit/Components/YUCPSolverModeCard.uxml");
            
            if (template != null)
            {
                template.CloneTree(this);
            }
            
            titleLabel = this.Q<Label>("solver-title");
            descriptionLabel = this.Q<Label>("solver-description");
            goodForLabel = this.Q<Label>("solver-good-for");
        }
        
        public void SetMode(SolverMode mode)
        {
            ClearModeClassList();
            
            string title = "";
            string description = "";
            string goodFor = "";
            
            switch (mode)
            {
                case SolverMode.Rigid:
                    AddToClassList("mode-rigid");
                    title = "Rigid Transform";
                    description = "Simple rotation + translation";
                    goodFor = "Piercings, small badges, hard jewelry";
                    break;
                case SolverMode.RigidNormalOffset:
                    AddToClassList("mode-rigid-normal");
                    title = "Rigid + Normal Offset";
                    description = "Rigid with outward push along surface normal";
                    goodFor = "Raised jewelry, studs, embellishments";
                    break;
                case SolverMode.Affine:
                    AddToClassList("mode-affine");
                    title = "Affine Transform";
                    description = "Allows minor shear/scale to match skin stretch";
                    goodFor = "Stickers, patches, wider decorations";
                    break;
                case SolverMode.CageRBF:
                    AddToClassList("mode-cage-rbf");
                    title = "Cage/RBF Deformation";
                    description = "Smooth deformation using driver points (advanced)";
                    goodFor = "Masks, large patches, complex meshes";
                    break;
            }
            
            if (titleLabel != null) titleLabel.text = title;
            if (descriptionLabel != null) descriptionLabel.text = description;
            if (goodForLabel != null) goodForLabel.text = $"Good for: {goodFor}";
        }
        
        private void ClearModeClassList()
        {
            RemoveFromClassList("mode-rigid");
            RemoveFromClassList("mode-rigid-normal");
            RemoveFromClassList("mode-affine");
            RemoveFromClassList("mode-cage-rbf");
        }
    }
}

