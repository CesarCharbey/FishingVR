using UnityEditor;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>Draws a [MinMax] Vector2 / Vector2Int as two fields labelled Min and Max.</summary>
    [CustomPropertyDrawer(typeof(MinMaxAttribute))]
    public class MinMaxDrawer : PropertyDrawer
    {
        static readonly GUIContent[] Labels = { new("Min"), new("Max") };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector2:
                {
                    var values = new[] { property.vector2Value.x, property.vector2Value.y };
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.MultiFloatField(EditorGUI.PrefixLabel(position, label), Labels, values);
                    if (EditorGUI.EndChangeCheck())
                        property.vector2Value = new Vector2(values[0], values[1]);
                    break;
                }
                case SerializedPropertyType.Vector2Int:
                {
                    var values = new[] { property.vector2IntValue.x, property.vector2IntValue.y };
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.MultiIntField(EditorGUI.PrefixLabel(position, label), Labels, values);
                    if (EditorGUI.EndChangeCheck())
                        property.vector2IntValue = new Vector2Int(values[0], values[1]);
                    break;
                }
                default:
                    EditorGUI.PropertyField(position, property, label);
                    break;
            }
            EditorGUI.EndProperty();
        }
    }
}
