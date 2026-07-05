using GameCreator.Editor.Common;
using GameCreator.Runtime.Characters.IK;
using UnityEditor;
using UnityEngine.UIElements;

namespace GameCreator.Editor.Characters
{
    [CustomPropertyDrawer(typeof(RigAlignGround))]
    public class RigAlignGroundDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty smoothTime = property.FindPropertyRelative("m_SmoothTime");
            SerializedProperty maxAngle = property.FindPropertyRelative("m_MaxAngle");
            
            VisualElement root = new VisualElement();
            
            root.Add(new PropertyTool(smoothTime));
            root.Add(new PropertyTool(maxAngle));

            return root;
        }
    }
}