using UnityEngine;

namespace ZStudio.CameraScaler {
    /// <summary>
    /// 根据参考分辨率和当前相机宽高比，自动调整正交相机的 Size 或透视相机的垂直 FOV。
    /// </summary>
    /// <remarks>
    /// 组件会在 Awake 中记录相机的初始 Size/FOV，并在其他组件的 Start 执行前完成首次适配。
    /// 运行期间应通过 <see cref="CameraZoom"/> 缩放，不要直接修改 Camera 的 Size/FOV。
    /// 所有公开 API 都必须在 Unity 主线程调用。
    /// </remarks>
    [AddComponentMenu("Layout/Camera Scaler")]
    [RequireComponent(typeof(Camera))]
    public class CameraScaler : MonoBehaviour {
        private const float k_DefaultReferenceWidth = 720f;
        private const float k_DefaultReferenceHeight = 1280f;
        private const float k_DefaultOrthographicSize = 5f;
        private const float k_DefaultFieldOfView = 60f;
        private const float k_MinimumFieldOfView = 1f;
        private const float k_MaximumFieldOfView = 179f;
        private const float k_MinimumAspect = 0.01f;
        private const float k_MaximumAspect = 100f;

        /// <summary>设计和调试游戏内容时使用的参考分辨率。</summary>
        [Tooltip("设计内容时使用的参考分辨率，宽和高必须大于 0。")]
        [SerializeField]
        private Vector2 m_ReferenceResolution = new(k_DefaultReferenceWidth, k_DefaultReferenceHeight);

        /// <summary>屏幕宽高比变化时采用的相机适配策略。</summary>
        [SerializeField]
        private EWorkingMode m_Mode = EWorkingMode.ConstantWidth;

        /// <summary>宽度匹配和高度匹配之间的插值权重：0 为宽度，1 为高度。</summary>
        [Range(0f, 1f)]
        [SerializeField]
        private float m_MatchWidthOrHeight = 0.5f;

        // 初始相机参数只缓存一次，避免适配结果被再次当作基准而产生累积误差。
        private Camera m_ComponentCamera;
        private float m_TargetAspect;
        private float m_CameraZoom = 1f;
        private float m_InitialSize;
        private float m_InitialFov;
        private float m_HorizontalFov;
        private bool m_IsInitialized;
        private bool m_HasApplied;

        // 上一次完成适配时的全部输入，用于避免每帧重复计算。
        private float m_PreviousUpdateAspect;
        private EWorkingMode m_PreviousUpdateMode;
        private float m_PreviousUpdateMatch;
        private Vector2 m_PreviousReferenceResolution;
        private bool m_PreviousOrthographic;

        /// <summary>当前参考分辨率。非法分量会被修正为 1。</summary>
        public Vector2 ReferenceResolution {
            get => m_ReferenceResolution;
            set {
                Vector2 sanitized = SanitizeReferenceResolution(value);

                if (Approximately(m_ReferenceResolution, sanitized)) {
                    return;
                }

                m_ReferenceResolution = sanitized;
                RefreshIfInitialized();
            }
        }

        /// <summary>当前相机适配策略。</summary>
        public EWorkingMode WorkingMode {
            get => m_Mode;
            set {
                if (!IsValidMode(value)) {
                    Debug.LogError($"无效的 CameraScaler 工作模式：{value}。", this);
                    return;
                }

                if (m_Mode == value) {
                    return;
                }

                m_Mode = value;
                RefreshIfInitialized();
            }
        }

        /// <summary>宽度与高度的匹配权重。赋值会被限制到 0～1。</summary>
        public float MatchWidthOrHeight {
            get => m_MatchWidthOrHeight;
            set {
                float sanitized = SanitizeMatch(value);

                if (Mathf.Approximately(m_MatchWidthOrHeight, sanitized)) {
                    return;
                }

                m_MatchWidthOrHeight = sanitized;
                RefreshIfInitialized();
            }
        }

        /// <summary>参考分辨率下、未应用 <see cref="CameraZoom"/> 时的正交相机水平半尺寸。</summary>
        public float HorizontalSize {
            get {
                EnsureInitialized();
                return m_InitialSize * m_TargetAspect;
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
                if (!IsFinitePositive(value)) {
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

        /// <summary>屏幕宽高比变化时可采用的适配策略。</summary>
        public enum EWorkingMode {
            /// <summary>保持参考分辨率下的垂直可视范围。</summary>
            ConstantHeight,

            /// <summary>保持参考分辨率下的水平可视范围。</summary>
            ConstantWidth,

            /// <summary>在保持宽度和保持高度之间按权重插值。</summary>
            MatchWidthOrHeight,

            /// <summary>确保参考分辨率内的区域始终可见，必要时扩展额外可视区域。</summary>
            Expand,

            /// <summary>避免显示参考分辨率之外的区域，必要时裁减可视区域。</summary>
            Shrink
        }

        /// <summary>缓存初始相机参数，并在其他组件的 Start 之前完成首次适配。</summary>
        private void Awake() {
            EnsureInitialized();
            RefreshCamera(true);
        }

        /// <summary>组件重新启用后检查期间发生的相机状态变化。</summary>
        private void OnEnable() {
            EnsureInitialized();
            RefreshCamera(true);
        }

        /// <summary>仅在影响适配结果的输入发生变化时重新计算相机参数。</summary>
        private void Update() => RefreshCamera(false);

        /// <summary>在 Inspector 修改数据时立即修正非法序列化值。</summary>
        private void OnValidate() {
            SanitizeSerializedFields();

            if (Application.isPlaying && isActiveAndEnabled) {
                EnsureInitialized();
                RefreshCamera(true);
            }
        }

        /// <summary>
        /// 立即按照相机的当前宽高比和投影类型重新应用适配。
        /// 外部代码直接修改 Camera 配置后可主动调用此方法。
        /// </summary>
        public void Refresh() {
            EnsureInitialized();
            RefreshCamera(true);
        }

        private void EnsureInitialized() {
            if (m_IsInitialized) {
                return;
            }

            SanitizeSerializedFields();
            m_ComponentCamera = GetComponent<Camera>();

            m_InitialSize = IsFinitePositive(m_ComponentCamera.orthographicSize)
                ? m_ComponentCamera.orthographicSize
                : k_DefaultOrthographicSize;

            m_InitialFov = SanitizeFieldOfView(m_ComponentCamera.fieldOfView);
            UpdateReferenceData();
            m_IsInitialized = true;
        }

        private void RefreshIfInitialized() {
            if (m_IsInitialized) {
                RefreshCamera(true);
            }
        }

        private void RefreshCamera(bool force) {
            if (!m_IsInitialized) {
                return;
            }

            SanitizeSerializedFields();
            float currentAspect = GetSafeAspect(m_ComponentCamera.aspect);
            bool referenceChanged = !Approximately(m_PreviousReferenceResolution, m_ReferenceResolution);

            if (!force
                && m_HasApplied
                && !referenceChanged
                && Mathf.Approximately(m_PreviousUpdateAspect, currentAspect)
                && m_PreviousUpdateMode == m_Mode
                && Mathf.Approximately(m_PreviousUpdateMatch, m_MatchWidthOrHeight)
                && m_PreviousOrthographic == m_ComponentCamera.orthographic) {
                return;
            }

            if (referenceChanged || !m_HasApplied) {
                UpdateReferenceData();
            }

            if (m_ComponentCamera.orthographic) {
                UpdateOrtho(currentAspect);
            } else {
                UpdatePerspective(currentAspect);
            }

            m_PreviousUpdateAspect = currentAspect;
            m_PreviousUpdateMode = m_Mode;
            m_PreviousUpdateMatch = m_MatchWidthOrHeight;
            m_PreviousReferenceResolution = m_ReferenceResolution;
            m_PreviousOrthographic = m_ComponentCamera.orthographic;
            m_HasApplied = true;
        }

        private void UpdateReferenceData() {
            m_TargetAspect = CalculateAspect(m_ReferenceResolution);
            m_HorizontalFov = CalcHorizontalFov(m_InitialFov, m_TargetAspect);
        }

        /// <summary>更新正交相机的垂直半尺寸。</summary>
        private void UpdateOrtho(float currentAspect) {
            float constantHeightSize = m_InitialSize;
            float constantWidthSize = m_InitialSize * (m_TargetAspect / currentAspect);
            float result;

            switch (m_Mode) {
                case EWorkingMode.ConstantHeight:
                    result = constantHeightSize;
                    break;
                case EWorkingMode.ConstantWidth:
                    result = constantWidthSize;
                    break;
                case EWorkingMode.MatchWidthOrHeight:
                    result = GeometricLerp(constantWidthSize, constantHeightSize, m_MatchWidthOrHeight);
                    break;
                case EWorkingMode.Expand:
                    result = Mathf.Max(constantWidthSize, constantHeightSize);
                    break;
                case EWorkingMode.Shrink:
                    result = Mathf.Min(constantWidthSize, constantHeightSize);
                    break;
                default:
                    result = constantWidthSize;
                    break;
            }

            m_ComponentCamera.orthographicSize = Mathf.Max(result / m_CameraZoom, Mathf.Epsilon);
        }

        /// <summary>更新透视相机的垂直视野角。</summary>
        private void UpdatePerspective(float currentAspect) {
            float constantHeightScale = FovToProjectionScale(m_InitialFov);
            float constantWidthFov = CalcVerticalFov(m_HorizontalFov, currentAspect);
            float constantWidthScale = FovToProjectionScale(constantWidthFov);
            float resultScale;

            switch (m_Mode) {
                case EWorkingMode.ConstantHeight:
                    resultScale = constantHeightScale;
                    break;
                case EWorkingMode.ConstantWidth:
                    resultScale = constantWidthScale;
                    break;
                case EWorkingMode.MatchWidthOrHeight:
                    // 对投影平面尺度做几何插值，才能与 CanvasScaler 的对数缩放语义一致。
                    resultScale = GeometricLerp(
                        constantWidthScale,
                        constantHeightScale,
                        m_MatchWidthOrHeight
                    );

                    break;
                case EWorkingMode.Expand:
                    resultScale = Mathf.Max(constantWidthScale, constantHeightScale);
                    break;
                case EWorkingMode.Shrink:
                    resultScale = Mathf.Min(constantWidthScale, constantHeightScale);
                    break;
                default:
                    resultScale = constantWidthScale;
                    break;
            }

            // Zoom 作用于投影平面尺度，避免直接 FOV/Zoom 带来的非线性误差。
            m_ComponentCamera.fieldOfView = ProjectionScaleToFov(resultScale / m_CameraZoom);
        }

        private void SanitizeSerializedFields() {
            m_ReferenceResolution = SanitizeReferenceResolution(m_ReferenceResolution);
            m_MatchWidthOrHeight = SanitizeMatch(m_MatchWidthOrHeight);

            if (!IsValidMode(m_Mode)) {
                m_Mode = EWorkingMode.ConstantWidth;
            }
        }

        private float GetSafeAspect(float aspect) {
            return IsFinitePositive(aspect) ? Mathf.Clamp(aspect, k_MinimumAspect, k_MaximumAspect) : m_TargetAspect;
        }

        private static Vector2 SanitizeReferenceResolution(Vector2 resolution) {
            return new Vector2(
                SanitizeDimension(resolution.x),
                SanitizeDimension(resolution.y)
            );
        }

        private static float SanitizeDimension(float value) {
            return IsFinitePositive(value) ? value : 1f;
        }

        private static float CalculateAspect(Vector2 resolution) {
            double aspect = (double)resolution.x / resolution.y;

            if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0d) {
                return k_DefaultReferenceWidth / k_DefaultReferenceHeight;
            }

            return Mathf.Clamp((float)aspect, k_MinimumAspect, k_MaximumAspect);
        }

        private static float SanitizeMatch(float value) {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0.5f;
        }

        private static float SanitizeFieldOfView(float value) {
            return IsFinite(value) ? Mathf.Clamp(value, k_MinimumFieldOfView, k_MaximumFieldOfView) : k_DefaultFieldOfView;
        }

        private static bool IsValidMode(EWorkingMode mode) {
            return mode >= EWorkingMode.ConstantHeight && mode <= EWorkingMode.Shrink;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;

        private static bool Approximately(Vector2 left, Vector2 right) {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }

        private static float GeometricLerp(float from, float to, float t) {
            float fromLog = Mathf.Log(from, 2f);
            float toLog = Mathf.Log(to, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(fromLog, toLog, t));
        }

        /// <summary>将水平视野角转换为指定宽高比下的垂直视野角。</summary>
        private static float CalcVerticalFov(float horizontalFovInDegrees, float aspectRatio) {
            float horizontalScale = FovToProjectionScale(horizontalFovInDegrees);
            return ProjectionScaleToFov(horizontalScale / aspectRatio);
        }

        /// <summary>将垂直视野角转换为指定宽高比下的水平视野角。</summary>
        private static float CalcHorizontalFov(float verticalFovInDegrees, float aspectRatio) {
            float verticalScale = FovToProjectionScale(verticalFovInDegrees);
            return ProjectionScaleToFov(verticalScale * aspectRatio);
        }

        private static float FovToProjectionScale(float fovInDegrees) {
            return Mathf.Tan(fovInDegrees * Mathf.Deg2Rad * 0.5f);
        }

        private static float ProjectionScaleToFov(float projectionScale) {
            float fov = 2f * Mathf.Atan(projectionScale) * Mathf.Rad2Deg;
            return Mathf.Clamp(fov, k_MinimumFieldOfView, k_MaximumFieldOfView);
        }
    }
}