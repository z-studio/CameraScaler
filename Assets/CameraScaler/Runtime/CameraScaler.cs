using UnityEngine;

namespace ZStudio.CameraScaler {
    /// <summary>
    /// 根据参考分辨率和当前相机宽高比，自动调整正交相机的 Size 或透视相机的垂直 FOV。
    /// </summary>
    /// <remarks>
    /// 设计基准是组件上的参考 Size/FOV，而不是 Camera 在 Awake 时的瞬时值。
    /// Play Mode 下会在 OnEnable 中完成首次适配，因此其他组件可在 Start 中读取结果。
    /// 运行期间应通过 <see cref="CameraZoom"/> 缩放，不要直接修改 Camera 的 Size/FOV。
    /// 所有公开 API 都必须在 Unity 主线程调用。
    /// </remarks>
    [AddComponentMenu("Layout/ZStudio/Camera Scaler")]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class CameraScaler : MonoBehaviour {
        private const int k_CurrentSerializedVersion = 1;

        /// <summary>设计和调试游戏内容时使用的参考分辨率。</summary>
        [Tooltip("设计内容时使用的参考分辨率，宽和高必须大于 0。")]
        [SerializeField]
        private Vector2 m_ReferenceResolution = new(
            CameraScalerMath.kDefaultReferenceWidth,
            CameraScalerMath.kDefaultReferenceHeight
        );

        /// <summary>屏幕宽高比变化时采用的相机适配策略。</summary>
        [Tooltip("屏幕宽高比变化时采用的相机适配策略。")]
        [SerializeField]
        private EScaleMode m_ScaleMode = EScaleMode.ConstantWidth;

        /// <summary>宽度匹配和高度匹配之间的插值权重：0 为宽度，1 为高度。</summary>
        [Tooltip("宽度匹配和高度匹配之间的插值权重：0 为宽度，1 为高度。")]
        [Range(0f, 1f)]
        [SerializeField]
        private float m_MatchWidthOrHeight = 0.5f;

        /// <summary>参考分辨率下的正交相机垂直半尺寸。</summary>
        [Tooltip("参考分辨率下的正交相机垂直半尺寸。运行时以此为基准，而不是 Camera 上的当前 Size。")]
        [SerializeField]
        private float m_ReferenceOrthographicSize = CameraScalerMath.kDefaultOrthographicSize;

        /// <summary>参考分辨率下的透视相机垂直视野角。</summary>
        [Tooltip("参考分辨率下的透视相机垂直视野角。运行时以此为基准，而不是 Camera 上的当前 FOV。")]
        [Range(1f, 179f)]
        [SerializeField]
        private float m_ReferenceFieldOfView = CameraScalerMath.kDefaultFieldOfView;

        /// <summary>相对于基准投影视野的缩放倍率。</summary>
        [Tooltip("相对于基准投影视野的缩放倍率。1 表示原始视野，大于 1 表示放大。")]
        [Min(0.0001f)]
        [SerializeField]
        private float m_CameraZoom = 1f;

        /// <summary>将适配结果写入 Camera 的时机。</summary>
        [Tooltip("将适配结果写入 Camera 的时机。与 Cinemachine 等系统冲突时，可改为 Late Update 或 On Pre Cull。")]
        [SerializeField]
        private EApplyTiming m_ApplyTiming = EApplyTiming.Update;

        [Tooltip("在编辑模式下预览适配效果；关闭预览或禁用组件时恢复原相机参数。")]
        [SerializeField]
        private bool m_PreviewInEditMode;

#if UNITY_EDITOR
        private bool m_HasPreviewSnapshot;
        private float m_PreviewOriginalSize;
        private float m_PreviewOriginalFov;
#endif

        [SerializeField, HideInInspector]
        private int m_SerializedVersion;

        private Camera m_ComponentCamera;
        private float m_TargetAspect;
        private float m_HorizontalFov;
        private bool m_IsInitialized;
        private bool m_HasApplied;

        private float m_PreviousUpdateAspect;
        private EScaleMode m_PreviousUpdateMode;
        private float m_PreviousUpdateMatch;
        private Vector2 m_PreviousReferenceResolution;
        private float m_PreviousReferenceSize;
        private float m_PreviousReferenceFov;
        private float m_PreviousCameraZoom;
        private bool m_PreviousOrthographic;

        /// <summary>当前参考分辨率。非法分量会被修正为 1。</summary>
        public Vector2 ReferenceResolution {
            get => m_ReferenceResolution;
            set {
                Vector2 sanitized = CameraScalerMath.SanitizeReferenceResolution(value);

                if (CameraScalerMath.Approximately(m_ReferenceResolution, sanitized)) {
                    return;
                }

                m_ReferenceResolution = sanitized;
                RefreshIfInitialized();
            }
        }

        /// <summary>当前相机适配策略。</summary>
        public EScaleMode ScaleMode {
            get => m_ScaleMode;
            set {
                if (!CameraScalerMath.IsValidScaleMode(value)) {
                    Debug.LogError($"无效的 CameraScaler 工作模式：{value}。", this);
                    return;
                }

                if (m_ScaleMode == value) {
                    return;
                }

                m_ScaleMode = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>宽度与高度的匹配权重。赋值会被限制到 0～1。</summary>
        public float MatchWidthOrHeight {
            get => m_MatchWidthOrHeight;
            set {
                float sanitized = CameraScalerMath.SanitizeMatch(value);

                if (Mathf.Approximately(m_MatchWidthOrHeight, sanitized)) {
                    return;
                }

                m_MatchWidthOrHeight = sanitized;
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下的正交相机垂直半尺寸。非法值会被修正为 5。</summary>
        public float ReferenceOrthographicSize {
            get => m_ReferenceOrthographicSize;
            set {
                float sanitized = CameraScalerMath.SanitizeOrthographicSize(value);

                if (Mathf.Approximately(m_ReferenceOrthographicSize, sanitized)) {
                    return;
                }

                m_ReferenceOrthographicSize = sanitized;
                MarkBaselineSerialized();
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下的透视相机垂直视野角。非法值会被限制到 1～179。</summary>
        public float ReferenceFieldOfView {
            get => m_ReferenceFieldOfView;
            set {
                float sanitized = CameraScalerMath.SanitizeFieldOfView(value);

                if (Mathf.Approximately(m_ReferenceFieldOfView, sanitized)) {
                    return;
                }

                m_ReferenceFieldOfView = sanitized;
                MarkBaselineSerialized();
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下、未应用 <see cref="CameraZoom"/> 时的正交相机水平半尺寸。</summary>
        public float HorizontalSize {
            get {
                EnsureInitialized();
                return m_ReferenceOrthographicSize * m_TargetAspect;
            }
        }

        /// <summary>参考分辨率下、未应用 <see cref="CameraZoom"/> 时的水平视野角。</summary>
        public float HorizontalFov {
            get {
                EnsureInitialized();
                return m_HorizontalFov;
            }
        }

        /// <summary>
        /// 相对于初始投影视野的缩放倍率。1 表示原始视野，大于 1 表示放大。
        /// </summary>
        /// <remarks>
        /// 透视相机按投影平面比例缩放，而不是直接将角度相除。非法值会被拒绝并保留原值。
        /// </remarks>
        public float CameraZoom {
            get => m_CameraZoom;
            set {
                if (!CameraScalerMath.IsFinitePositive(value)) {
                    Debug.LogError($"CameraZoom 必须是大于 0 的有限值，当前输入：{value}。", this);
                    return;
                }

                if (Mathf.Approximately(m_CameraZoom, value)) {
                    return;
                }

                m_CameraZoom = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>将适配结果写入 Camera 的时机。</summary>
        public EApplyTiming ApplyTiming {
            get => m_ApplyTiming;
            set {
                if (!CameraScalerMath.IsValidApplyTiming(value)) {
                    Debug.LogError($"无效的 CameraScaler 写入时机：{value}。", this);
                    return;
                }

                if (m_ApplyTiming == value) {
                    return;
                }

                m_ApplyTiming = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>是否在编辑模式自动预览适配。默认关闭，不影响运行时适配。</summary>
        public bool PreviewInEditMode {
            get => m_PreviewInEditMode;
            set {
                m_PreviewInEditMode = value;
#if UNITY_EDITOR
                UpdateEditorPreview();
#endif
            }
        }

        /// <summary>缓存参考数据，首次适配改由 OnEnable 完成，避免与 OnEnable 重复写入。</summary>
        private void Awake() {
            EnsureInitialized();
        }

        /// <summary>Play Mode 下启用后立即应用适配，供其他脚本在 Start 中读取。</summary>
        private void OnEnable() {
            EnsureInitialized();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= UpdateEditorPreview;
            UnityEditor.EditorApplication.update += UpdateEditorPreview;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UpdateEditorPreview();
#endif

            if (Application.IsPlaying(gameObject)) {
                RefreshCamera(true);
            }
        }

        /// <summary>仅在影响适配结果的输入发生变化时重新计算相机参数。</summary>
        private void Update() {
            if (Application.IsPlaying(gameObject) && m_ApplyTiming == EApplyTiming.Update) {
                RefreshCamera(false);
            }
        }

        private void LateUpdate() {
            if (Application.IsPlaying(gameObject) && m_ApplyTiming == EApplyTiming.LateUpdate) {
                RefreshCamera(false);
            }
        }

        private void OnPreCull() {
            if (Application.IsPlaying(gameObject) && m_ApplyTiming == EApplyTiming.OnPreCull) {
                RefreshCamera(false);
            }
        }

        private void OnDisable() {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= UpdateEditorPreview;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            RestoreEditorPreview();
#endif
        }

#if UNITY_EDITOR
        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) {
            if (gameObject.scene == scene) {
                RestoreEditorPreview();
            }
        }

        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state) {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode) {
                RestoreEditorPreview();
            } else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode) {
                UpdateEditorPreview();
            }
        }

        private void UpdateEditorPreview() {
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }

            if (!m_PreviewInEditMode || !isActiveAndEnabled) {
                RestoreEditorPreview();
                return;
            }

            EnsureInitialized();
            float previousSize = m_ComponentCamera.orthographicSize;
            float previousFov = m_ComponentCamera.fieldOfView;
            RefreshCamera(false);

            if (!Mathf.Approximately(previousSize, m_ComponentCamera.orthographicSize)
                || !Mathf.Approximately(previousFov, m_ComponentCamera.fieldOfView)) {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
        }

        private void RestoreEditorPreview() {
            if (!m_HasPreviewSnapshot) {
                return;
            }

            if (m_ComponentCamera != null) {
                m_ComponentCamera.orthographicSize = m_PreviewOriginalSize;
                m_ComponentCamera.fieldOfView = m_PreviewOriginalFov;
            }

            m_HasPreviewSnapshot = false;
            m_HasApplied = false;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        /// <summary>在 Inspector 修改数据时立即修正非法序列化值。</summary>
        private void OnValidate() {
            SanitizeSerializedFields();
            TryMigrateBaselineFromCamera();

            if (Application.IsPlaying(gameObject) && isActiveAndEnabled) {
                EnsureInitialized();
                RefreshCamera(true);
            }
        }

        private void Reset() {
            m_ComponentCamera = GetComponent<Camera>();
            CopyBaselineFromCamera();
        }

        /// <summary>
        /// 立即按照相机的当前宽高比和投影类型重新应用适配。
        /// 外部代码直接修改 Camera 配置后可主动调用此方法。
        /// 此方法不会把 Camera 的当前 Size/FOV 重新定义为基准，需要更新基准时请调用 <see cref="RecaptureBaseline"/>。
        /// </summary>
        public void Refresh() {
            EnsureInitialized();
            RefreshCamera(true);
        }

        /// <summary>
        /// 把 Camera 当前的 Size/FOV 采集为新的设计基准。
        /// 应在 Camera 已经处于「参考分辨率下的目标观感」时调用。
        /// 预览期间先恢复原相机参数再采集，避免使用适配后的结果；预览会在下一次编辑器更新时恢复。
        /// </summary>
        public void RecaptureBaseline() {
#if UNITY_EDITOR

            // 先恢复设计值，避免把已适配的预览结果重新采集为基准。
            RestoreEditorPreview();
#endif
            if (m_ComponentCamera == null) {
                m_ComponentCamera = GetComponent<Camera>();
            }

            CopyBaselineFromCamera();
            SanitizeSerializedFields();
            UpdateReferenceData();
            m_IsInitialized = true;

            if (Application.IsPlaying(gameObject) && isActiveAndEnabled) {
                RefreshCamera(true);
            }
        }

        private void EnsureInitialized() {
            if (m_IsInitialized) {
                return;
            }

            SanitizeSerializedFields();
            m_ComponentCamera = GetComponent<Camera>();
            TryMigrateBaselineFromCamera();
            UpdateReferenceData();
            m_IsInitialized = true;
        }

        private void RefreshIfInitialized() {
            if (m_IsInitialized) {
                RefreshCamera(true);
            }
        }

        private void RefreshCamera(bool force) {
            if (!m_IsInitialized || m_ComponentCamera == null) {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && m_PreviewInEditMode) {
                if (!isActiveAndEnabled || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) {
                    return;
                }

                if (!m_HasPreviewSnapshot) {
                    m_PreviewOriginalSize = m_ComponentCamera.orthographicSize;
                    m_PreviewOriginalFov = m_ComponentCamera.fieldOfView;
                    m_HasPreviewSnapshot = true;
                    force = true;
                }
            }
#endif
            SanitizeSerializedFields();
            float currentAspect = CameraScalerMath.GetSafeAspect(m_ComponentCamera.aspect, m_TargetAspect);

            bool baselineChanged =
                !CameraScalerMath.Approximately(m_PreviousReferenceResolution, m_ReferenceResolution)
                || !Mathf.Approximately(m_PreviousReferenceSize, m_ReferenceOrthographicSize)
                || !Mathf.Approximately(m_PreviousReferenceFov, m_ReferenceFieldOfView);

            if (!force
                && m_HasApplied
                && !baselineChanged
                && Mathf.Approximately(m_PreviousUpdateAspect, currentAspect)
                && m_PreviousUpdateMode == m_ScaleMode
                && Mathf.Approximately(m_PreviousUpdateMatch, m_MatchWidthOrHeight)
                && Mathf.Approximately(m_PreviousCameraZoom, m_CameraZoom)
                && m_PreviousOrthographic == m_ComponentCamera.orthographic) {
                return;
            }

            if (baselineChanged || !m_HasApplied) {
                UpdateReferenceData();
            }

            if (m_ComponentCamera.orthographic) {
                m_ComponentCamera.orthographicSize = CameraScalerMath.CalculateOrthographicSize(
                    m_ReferenceOrthographicSize,
                    m_TargetAspect,
                    currentAspect,
                    m_ScaleMode,
                    m_MatchWidthOrHeight,
                    m_CameraZoom
                );
            } else {
                m_ComponentCamera.fieldOfView = CameraScalerMath.CalculateFieldOfView(
                    m_ReferenceFieldOfView,
                    m_TargetAspect,
                    currentAspect,
                    m_ScaleMode,
                    m_MatchWidthOrHeight,
                    m_CameraZoom
                );
            }

            m_PreviousUpdateAspect = currentAspect;
            m_PreviousUpdateMode = m_ScaleMode;
            m_PreviousUpdateMatch = m_MatchWidthOrHeight;
            m_PreviousReferenceResolution = m_ReferenceResolution;
            m_PreviousReferenceSize = m_ReferenceOrthographicSize;
            m_PreviousReferenceFov = m_ReferenceFieldOfView;
            m_PreviousCameraZoom = m_CameraZoom;
            m_PreviousOrthographic = m_ComponentCamera.orthographic;
            m_HasApplied = true;
        }

        private void UpdateReferenceData() {
            m_TargetAspect = CameraScalerMath.CalculateAspect(m_ReferenceResolution);
            m_HorizontalFov = CameraScalerMath.CalcHorizontalFov(m_ReferenceFieldOfView, m_TargetAspect);
        }

        private void SanitizeSerializedFields() {
            m_ReferenceResolution = CameraScalerMath.SanitizeReferenceResolution(m_ReferenceResolution);
            m_MatchWidthOrHeight = CameraScalerMath.SanitizeMatch(m_MatchWidthOrHeight);
            m_ReferenceOrthographicSize = CameraScalerMath.SanitizeOrthographicSize(m_ReferenceOrthographicSize);
            m_ReferenceFieldOfView = CameraScalerMath.SanitizeFieldOfView(m_ReferenceFieldOfView);
            m_CameraZoom = CameraScalerMath.SanitizeZoom(m_CameraZoom);

            if (!CameraScalerMath.IsValidScaleMode(m_ScaleMode)) {
                m_ScaleMode = EScaleMode.ConstantWidth;
            }

            if (!CameraScalerMath.IsValidApplyTiming(m_ApplyTiming)) {
                m_ApplyTiming = EApplyTiming.Update;
            }
        }

        private void TryMigrateBaselineFromCamera() {
            if (m_SerializedVersion >= k_CurrentSerializedVersion) {
                return;
            }

            if (m_ComponentCamera == null) {
                m_ComponentCamera = GetComponent<Camera>();
            }

            CopyBaselineFromCamera();
        }

        private void CopyBaselineFromCamera() {
            if (m_ComponentCamera == null) {
                return;
            }

            m_ReferenceOrthographicSize = CameraScalerMath.SanitizeOrthographicSize(m_ComponentCamera.orthographicSize);
            m_ReferenceFieldOfView = CameraScalerMath.SanitizeFieldOfView(m_ComponentCamera.fieldOfView);
            MarkBaselineSerialized();
        }

        private void MarkBaselineSerialized() {
            m_SerializedVersion = k_CurrentSerializedVersion;
        }
    }
}