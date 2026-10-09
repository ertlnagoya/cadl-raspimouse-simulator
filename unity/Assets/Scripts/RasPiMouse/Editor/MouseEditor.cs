using UnityEditor;
using UnityEngine;

namespace RasPiMouse.Editor
{
    [CustomEditor(typeof(Mouse))]
    public class MouseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var mouse = (Mouse) target;
            if (mouse.lineSensor)
            {
                EditorGUILayout.LabelField($"line  : " +
                                           $"{Round(mouse.lineSensor.Lightness(0))} " +
                                           $"{Round(mouse.lineSensor.Lightness(1))} " +
                                           $"{Round(mouse.lineSensor.Lightness(2))} " +
                                           $"{Round(mouse.lineSensor.Lightness(3))}");
            }

            if (mouse.distSensor)
            {
                EditorGUILayout.LabelField($"dist  : " +
                                           $"{Round(mouse.distSensor.Distance(0))} " +
                                           $"{Round(mouse.distSensor.Distance(1))} " +
                                           $"{Round(mouse.distSensor.Distance(2))} " +
                                           $"{Round(mouse.distSensor.Distance(3))}");
            }

            if (mouse.lidar)
            {
                EditorGUILayout.LabelField($"lidar : rad={Round(mouse.lidar.rad)} ,dist={Round(mouse.lidar.distance)}");
            }

            base.OnInspectorGUI();
        }

        private static string Round(float f)
        {
            return $"{Mathf.Round(100 * f) / 100f}".PadLeft(7);
        }
    }
}