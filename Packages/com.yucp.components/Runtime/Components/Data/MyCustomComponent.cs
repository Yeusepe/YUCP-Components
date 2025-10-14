using UnityEngine;
using VRC.SDKBase;

namespace YUCP.Components
{
    /// <summary>
    /// Example template for creating custom YUCP components.
    /// Implement build-time logic in OnPreprocess().
    /// </summary>
    [AddComponentMenu("YUCP/Examples/My Custom Component")]
    public class MyCustomComponent : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Tooltip("Example toggle flag")]
        public bool exampleFlag;

        public int PreprocessOrder => 0;

        public bool OnPreprocess()
        {
            bool flag = exampleFlag;

            return true;
        }
    }
}
