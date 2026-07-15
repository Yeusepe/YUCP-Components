using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.VisemePhrase
{
    internal interface IVisemePhraseEnrollmentDraft
    {
        Object TargetObject { get; }
        GameObject AvatarRoot { get; }
        string DisplayName { get; }
        string Prompt { get; set; }
        IReadOnlyList<VisemePhraseCapturedTake> Takes { get; }
        VisemePhraseCapturedTake NegativeSample { get; }
        Object ProfileAsset { get; }
        string AssetPath { get; }

        void SavePrompt();
        void SaveTake(int index, VisemePhraseCapturedTake take);
        void SaveNegativeSample(VisemePhraseCapturedTake take);
        void ClearNegativeSample();
    }
}
