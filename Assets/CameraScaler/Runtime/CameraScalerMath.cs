using UnityEngine;

namespace ZStudio.CameraScaler {
    /// <summary>
    /// Camera Scaler 的纯计算逻辑，不依赖 MonoBehaviour 生命周期。
    /// </summary>
    public static class CameraScalerMath {
        public const float kDefaultReferenceWidth = 720f;
        public const float kDefaultReferenceHeight = 1280f;
        public const float kDefaultOrthographicSize = 5f;
        public const float kDefaultFieldOfView = 60f;
        public const float kMinimumFieldOfView = 1f;
        public const float kMaximumFieldOfView = 179f;
        public const float kMinimumAspect = 0.01f;
        public const float kMaximumAspect = 100f;

        /// <summary>按适配模式计算正交相机的垂直半尺寸。</summary>
        public static float CalculateOrthographicSize(
            float referenceSize,
            float referenceAspect,
            float currentAspect,
            EScaleMode mode,
            float matchWidthOrHeight,
            float zoom
        ) {
            float safeReferenceAspect = GetSafeAspect(referenceAspect, kDefaultReferenceWidth / kDefaultReferenceHeight);
            float safeCurrentAspect = GetSafeAspect(currentAspect, safeReferenceAspect);
            float constantHeightSize = SanitizeOrthographicSize(referenceSize);
            float constantWidthSize = constantHeightSize * (safeReferenceAspect / safeCurrentAspect);
            float result = SelectFitValue(constantWidthSize, constantHeightSize, mode, matchWidthOrHeight);
            return Mathf.Max(result / SanitizeZoom(zoom), Mathf.Epsilon);
        }

        /// <summary>按适配模式计算透视相机的垂直视野角。</summary>
        public static float CalculateFieldOfView(
            float referenceVerticalFov,
            float referenceAspect,
            float currentAspect,
            EScaleMode mode,
            float matchWidthOrHeight,
            float zoom
        ) {
            float safeReferenceAspect = GetSafeAspect(referenceAspect, kDefaultReferenceWidth / kDefaultReferenceHeight);
            float safeCurrentAspect = GetSafeAspect(currentAspect, safeReferenceAspect);
            float safeReferenceFov = SanitizeFieldOfView(referenceVerticalFov);
            float horizontalFov = CalcHorizontalFov(safeReferenceFov, safeReferenceAspect);
            float constantHeightScale = FovToProjectionScale(safeReferenceFov);
            float constantWidthScale = FovToProjectionScale(CalcVerticalFov(horizontalFov, safeCurrentAspect));
            float resultScale = SelectFitValue(constantWidthScale, constantHeightScale, mode, matchWidthOrHeight);
            return ProjectionScaleToFov(resultScale / SanitizeZoom(zoom));
        }

        /// <summary>在宽度匹配值和高度匹配值之间按模式取值。</summary>
        public static float SelectFitValue(
            float constantWidth,
            float constantHeight,
            EScaleMode mode,
            float matchWidthOrHeight
        ) {
            return mode switch {
                EScaleMode.ConstantHeight => constantHeight,
                EScaleMode.ConstantWidth => constantWidth,
                EScaleMode.MatchWidthOrHeight => GeometricLerp(
                    constantWidth,
                    constantHeight,
                    SanitizeMatch(matchWidthOrHeight)
                ),
                EScaleMode.Expand => Mathf.Max(constantWidth, constantHeight),
                EScaleMode.Shrink => Mathf.Min(constantWidth, constantHeight),
                _ => constantWidth
            };
        }

        /// <summary>在对数空间插值，语义与 CanvasScaler 的 Match 一致。</summary>
        public static float GeometricLerp(float from, float to, float t) {
            if (!IsFinitePositive(from) || !IsFinitePositive(to)) {
                return Mathf.Lerp(from, to, t);
            }

            float fromLog = Mathf.Log(from, 2f);
            float toLog = Mathf.Log(to, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(fromLog, toLog, t));
        }

        /// <summary>将水平视野角转换为指定宽高比下的垂直视野角。</summary>
        public static float CalcVerticalFov(float horizontalFovInDegrees, float aspectRatio) {
            float safeAspect = GetSafeAspect(aspectRatio, 1f);
            float horizontalScale = FovToProjectionScale(horizontalFovInDegrees);
            return ProjectionScaleToFov(horizontalScale / safeAspect);
        }

        /// <summary>将垂直视野角转换为指定宽高比下的水平视野角。</summary>
        public static float CalcHorizontalFov(float verticalFovInDegrees, float aspectRatio) {
            float safeAspect = GetSafeAspect(aspectRatio, 1f);
            float verticalScale = FovToProjectionScale(verticalFovInDegrees);
            return ProjectionScaleToFov(verticalScale * safeAspect);
        }

        public static float FovToProjectionScale(float fovInDegrees) {
            return Mathf.Tan(SanitizeFieldOfView(fovInDegrees) * Mathf.Deg2Rad * 0.5f);
        }

        public static float ProjectionScaleToFov(float projectionScale) {
            if (!IsFinitePositive(projectionScale)) {
                return kMinimumFieldOfView;
            }

            float fov = 2f * Mathf.Atan(projectionScale) * Mathf.Rad2Deg;
            return Mathf.Clamp(fov, kMinimumFieldOfView, kMaximumFieldOfView);
        }

        public static Vector2 SanitizeReferenceResolution(Vector2 resolution) {
            return new Vector2(SanitizeDimension(resolution.x), SanitizeDimension(resolution.y));
        }

        public static float SanitizeDimension(float value) {
            return IsFinitePositive(value) ? value : 1f;
        }

        public static float CalculateAspect(Vector2 resolution) {
            if (!IsFinitePositive(resolution.x) || !IsFinitePositive(resolution.y)) {
                return kDefaultReferenceWidth / kDefaultReferenceHeight;
            }

            double aspect = (double)resolution.x / resolution.y;
            return Mathf.Clamp((float)aspect, kMinimumAspect, kMaximumAspect);
        }

        public static float SanitizeMatch(float value) {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0.5f;
        }

        public static float SanitizeFieldOfView(float value) {
            return IsFinite(value)
                ? Mathf.Clamp(value, kMinimumFieldOfView, kMaximumFieldOfView)
                : kDefaultFieldOfView;
        }

        public static float SanitizeOrthographicSize(float value) {
            return IsFinitePositive(value) ? value : kDefaultOrthographicSize;
        }

        public static float SanitizeZoom(float value) {
            return IsFinitePositive(value) ? value : 1f;
        }

        public static float GetSafeAspect(float aspect, float fallbackAspect) {
            return IsFinitePositive(aspect)
                ? Mathf.Clamp(aspect, kMinimumAspect, kMaximumAspect)
                : fallbackAspect;
        }

        public static bool IsValidScaleMode(EScaleMode mode) {
            return mode >= EScaleMode.ConstantHeight && mode <= EScaleMode.Shrink;
        }

        public static bool IsValidApplyTiming(EApplyTiming timing) {
            return timing >= EApplyTiming.Update && timing <= EApplyTiming.OnPreCull;
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;

        public static bool Approximately(Vector2 left, Vector2 right) {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }
    }
}
