using UnityEngine.UIElements;

namespace YUCP.Components.PackageGuardian.Editor.Controls
{
    /// <summary>
    /// Custom button control with icon support.
    /// </summary>
    public class PgButton : Button
    {
        public PgButton() : base()
        {
            AddToClassList("pg-button");
        }
        
        public PgButton(System.Action clickEvent) : base(clickEvent)
        {
            AddToClassList("pg-button");
        }
        
        public void SetPrimary()
        {
            AddToClassList("pg-button-primary");
        }
        
        public void SetDanger()
        {
            AddToClassList("pg-button-danger");
        }
    }
}

