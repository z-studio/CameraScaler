using UnityEditor;
using UnityEngine;

namespace ZStudio.CameraScaler.Editor {
    [CustomEditor(typeof(CameraScaler))]
    [CanEditMultipleObjects]
    public sealed class CameraScalerEditor : UnityEditor.Editor {
        private SerializedProperty m_ReferenceResolution;
        private SerializedProperty m_ScaleMode;
        private SerializedProperty m_MatchWidthOrHeight;
        private SerializedProperty m_ReferenceOrthographicSize;
        private SerializedProperty m_ReferenceFieldOfView;
        private SerializedProperty m_CameraZoom;
        private SerializedProperty m_ApplyTiming;
        private SerializedProperty m_PreviewInEditMode;
        private GUIStyle m_RightAlignedLabel;

        private void OnEnable() {
            m_ReferenceResolution = serializedObject.FindProperty(nameof(m_ReferenceResolution));
            m_ScaleMode = serializedObject.FindProperty(nameof(m_ScaleMode));
            m_MatchWidthOrHeight = serializedObject.FindProperty(nameof(m_MatchWidthOrHeight));
            m_ReferenceOrthographicSize = serializedObject.FindProperty(nameof(m_ReferenceOrthographicSize));
            m_ReferenceFieldOfView = serializedObject.FindProperty(nameof(m_ReferenceFieldOfView));
            m_CameraZoom = serializedObject.FindProperty(nameof(m_CameraZoom));
            m_ApplyTiming = serializedObject.FindProperty(nameof(m_ApplyTiming));
            m_PreviewInEditMode = serializedObject.FindProperty(nameof(m_PreviewInEditMode));
            m_RightAlignedLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_ReferenceResolution);

            if (!m_ReferenceResolution.hasMultipleDifferentValues) {
                Vector2 resolution = m_ReferenceResolution.vector2Value;

                if (!CameraScalerMath.IsFinitePositive(resolution.x) ||
                    !CameraScalerMath.IsFinitePositive(resolution.y)) {
                    EditorGUILayout.HelpBox("参考分辨率的宽和高必须大于 0，非法值将被自动修正为 1。", MessageType.Error);
                }
            }

            EditorGUILayout.PropertyField(m_ScaleMode);

            if (!m_ScaleMode.hasMultipleDifferentValues) {
                var scaleMode = (EScaleMode)m_ScaleMode.intValue;

                switch (scaleMode) {
                    case EScaleMode.ConstantHeight:
                        EditorGUILayout.HelpBox("保持相机的垂直可视范围；CameraZoom 仍然有效。", MessageType.Info);
                        break;
                    case EScaleMode.ConstantWidth:
                        EditorGUILayout.HelpBox("保持相机的水平可视范围；竖屏游戏常用此模式。", MessageType.Info);
                        break;

                    case EScaleMode.MatchWidthOrHeight: {
                        Rect r = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 12);
                        DualLabeledSlider(r, m_MatchWidthOrHeight, "Match", "Width", "Height");

                        EditorGUILayout.HelpBox(
                            "在固定宽度和固定高度之间按权重做对数插值，语义与 CanvasScaler 一致。",
                            MessageType.Info
                        );

                        break;
                    }

                    case EScaleMode.Expand:
                        EditorGUILayout.HelpBox("保证参考区域全部可见，设备多出的空间显示额外内容。", MessageType.Info);
                        break;
                    case EScaleMode.Shrink:
                        EditorGUILayout.HelpBox("不显示参考区域之外的内容，必要时裁减参考区域。", MessageType.Info);
                        break;
                }
            }

            EditorGUILayout.PropertyField(m_ReferenceOrthographicSize);
            EditorGUILayout.PropertyField(m_ReferenceFieldOfView);
            EditorGUILayout.PropertyField(m_CameraZoom);
            EditorGUILayout.PropertyField(m_ApplyTiming);
            EditorGUILayout.PropertyField(m_PreviewInEditMode);

            DrawCameraWarnings();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            if (GUILayout.Button("从 Camera 重新采集基准")) {
                RecaptureBaselines();
            }
        }

        private void DrawCameraWarnings() {
            if (targets.Length != 1) {
                return;
            }

            var scaler = (CameraScaler)target;
            var camera = scaler.GetComponent<Camera>();

            if (camera == null) {
                return;
            }

            if (camera.usePhysicalProperties) {
                EditorGUILayout.HelpBox(
                    "当前 Camera 启用了 Physical Camera。写入 FOV 可能与焦距/传感器参数互相覆盖，建议关闭 Physical Camera，或确保没有其他系统同时写入这些属性。",
                    MessageType.Warning
                );
            }

            if (Application.isPlaying || scaler.PreviewInEditMode) {
                return;
            }

            bool sizeMismatched = camera.orthographic &&
                                  !Mathf.Approximately(camera.orthographicSize, m_ReferenceOrthographicSize.floatValue);

            bool fovMismatched = !camera.orthographic &&
                                 !Mathf.Approximately(camera.fieldOfView, m_ReferenceFieldOfView.floatValue);

            if (sizeMismatched || fovMismatched) {
                EditorGUILayout.HelpBox(
                    "Camera 的 Size/FOV 与 Camera Scaler 的设计基准不一致。Play 时以 Camera Scaler 的基准为准。可点击下方按钮重新采集。",
                    MessageType.Info
                );
            }
        }

        private void RecaptureBaselines() {
            foreach (Object targetObject in targets) {
                CameraScaler scaler = (CameraScaler)targetObject;
                Undo.RecordObject(scaler, "Recapture Camera Scaler Baseline");
                scaler.RecaptureBaseline();
                EditorUtility.SetDirty(scaler);
            }

            serializedObject.Update();
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
    }
}