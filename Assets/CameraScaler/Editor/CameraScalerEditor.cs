using UnityEditor;
using UnityEngine;

namespace ZStudio.CameraScaler.Editor {
    [CustomEditor(typeof(ZStudio.CameraScaler.CameraScaler))]
    [CanEditMultipleObjects]
    public sealed class CameraScalerEditor : UnityEditor.Editor {
        private SerializedProperty m_ReferenceResolution;
        private SerializedProperty m_Mode;
        private SerializedProperty m_MatchWidthOrHeight;
        private GUIStyle m_RightAlignedLabel;

        private void OnEnable() {
            m_ReferenceResolution = serializedObject.FindProperty(nameof(m_ReferenceResolution));
            m_Mode = serializedObject.FindProperty(nameof(m_Mode));
            m_MatchWidthOrHeight = serializedObject.FindProperty(nameof(m_MatchWidthOrHeight));
            m_RightAlignedLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_ReferenceResolution);

            if (!m_ReferenceResolution.hasMultipleDifferentValues) {
                Vector2 resolution = m_ReferenceResolution.vector2Value;

                if (!IsFinitePositive(resolution.x) || !IsFinitePositive(resolution.y)) {
                    EditorGUILayout.HelpBox("参考分辨率的宽和高必须大于 0，非法值将被自动修正为 1。", MessageType.Error);
                }
            }

            EditorGUILayout.PropertyField(m_Mode);

            if (!m_Mode.hasMultipleDifferentValues) {
                ZStudio.CameraScaler.CameraScaler.EWorkingMode workingMode = (ZStudio.CameraScaler.CameraScaler.EWorkingMode)m_Mode.enumValueIndex;

                switch (workingMode) {
                    case ZStudio.CameraScaler.CameraScaler.EWorkingMode.ConstantHeight: {
                        const string msg = "保持相机的垂直可视范围；CameraZoom 仍然有效。";
                        EditorGUILayout.HelpBox(msg, MessageType.Info);
                        break;
                    }

                    case ZStudio.CameraScaler.CameraScaler.EWorkingMode.MatchWidthOrHeight: {
                        Rect r = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 12);
                        DualLabeledSlider(r, m_MatchWidthOrHeight, "Match", "Width", "Height");
                        break;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DualLabeledSlider(
            Rect position,
            SerializedProperty property,
            string mainLabel,
            string labelLeft,
            string labelRight
        ) {
            position.height = EditorGUIUtility.singleLineHeight;
            Rect pos = position;

            position.y += 12;
            position.xMin += EditorGUIUtility.labelWidth;
            position.xMax -= EditorGUIUtility.fieldWidth;

            GUI.Label(position, labelLeft, EditorStyles.label);
            GUI.Label(position, labelRight, m_RightAlignedLabel);

            EditorGUI.Slider(pos, property, 0, 1, mainLabel);
        }

        private static bool IsFinitePositive(float value) {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }
}