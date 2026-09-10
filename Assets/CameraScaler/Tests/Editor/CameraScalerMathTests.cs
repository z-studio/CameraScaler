using NUnit.Framework;
using UnityEngine;

namespace ZStudio.CameraScaler.Tests.Editor {
    [TestFixture]
    public sealed class CameraScalerMathTests {
        [Test]
        public void Expand_Orthographic_UsesLargerFitValue() {
            float size = CameraScalerMath.CalculateOrthographicSize(
                5f,
                9f / 16f,
                16f / 9f,
                EScaleMode.Expand,
                0.5f,
                1f
            );

            Assert.That(size, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void Shrink_Orthographic_UsesSmallerFitValue() {
            float size = CameraScalerMath.CalculateOrthographicSize(
                5f,
                9f / 16f,
                16f / 9f,
                EScaleMode.Shrink,
                0.5f,
                1f
            );

            float expectedWidthSize = 5f * ((9f / 16f) / (16f / 9f));
            Assert.That(size, Is.EqualTo(expectedWidthSize).Within(0.0001f));
        }

        [Test]
        public void MatchWidthOrHeight_Orthographic_UsesGeometricInterpolation() {
            float referenceAspect = 16f / 9f;
            float currentAspect = 9f / 16f;
            float constantHeight = 5f;
            float constantWidth = 5f * (referenceAspect / currentAspect);
            float expected = Mathf.Sqrt(constantWidth * constantHeight);

            float size = CameraScalerMath.CalculateOrthographicSize(
                5f,
                referenceAspect,
                currentAspect,
                EScaleMode.MatchWidthOrHeight,
                0.5f,
                1f
            );

            Assert.That(size, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void GeometricLerp_AtEndpoints_ReturnsOriginalValues() {
            Assert.That(CameraScalerMath.GeometricLerp(2f, 8f, 0f), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(CameraScalerMath.GeometricLerp(2f, 8f, 1f), Is.EqualTo(8f).Within(0.0001f));
            Assert.That(CameraScalerMath.GeometricLerp(2f, 8f, 0.5f), Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void ExtremeZoom_ClampsPerspectiveFovToUnityLimits() {
            float minFov = CameraScalerMath.CalculateFieldOfView(
                60f,
                16f / 9f,
                16f / 9f,
                EScaleMode.ConstantHeight,
                0.5f,
                10000f
            );
            float maxFov = CameraScalerMath.CalculateFieldOfView(
                60f,
                16f / 9f,
                16f / 9f,
                EScaleMode.ConstantHeight,
                0.5f,
                0.0001f
            );

            Assert.That(minFov, Is.EqualTo(CameraScalerMath.kMinimumFieldOfView).Within(0.0001f));
            Assert.That(maxFov, Is.EqualTo(CameraScalerMath.kMaximumFieldOfView).Within(0.0001f));
        }

        [Test]
        public void Expand_Perspective_UsesLargerProjectionScale() {
            float fov = CameraScalerMath.CalculateFieldOfView(
                60f,
                9f / 16f,
                16f / 9f,
                EScaleMode.Expand,
                0.5f,
                1f
            );

            Assert.That(fov, Is.EqualTo(60f).Within(0.0001f));
        }

        [Test]
        public void InvalidResolution_FallsBackToSafeAspectAndDimensions() {
            Vector2 sanitized = CameraScalerMath.SanitizeReferenceResolution(
                new Vector2(0f, float.PositiveInfinity)
            );
            float aspect = CameraScalerMath.CalculateAspect(new Vector2(float.NaN, -4f));

            Assert.That(sanitized, Is.EqualTo(Vector2.one));
            Assert.That(
                aspect,
                Is.EqualTo(
                    CameraScalerMath.kDefaultReferenceWidth / CameraScalerMath.kDefaultReferenceHeight
                ).Within(0.0001f)
            );
        }

        [Test]
        public void HorizontalFov_MatchesProjectionScaleConversion() {
            float expected = CameraScalerMath.ProjectionScaleToFov(
                CameraScalerMath.FovToProjectionScale(60f) * (720f / 1280f)
            );

            Assert.That(CameraScalerMath.CalcHorizontalFov(60f, 720f / 1280f), Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(0f)]
        [TestCase(-4f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void CalculateAspect_WithInvalidDimension_ReturnsDefaultAspect(float invalidDimension) {
            float expected = CameraScalerMath.kDefaultReferenceWidth / CameraScalerMath.kDefaultReferenceHeight;

            Assert.That(CameraScalerMath.CalculateAspect(new Vector2(invalidDimension, 1280f)),
                Is.EqualTo(expected).Within(0.0001f));
            Assert.That(CameraScalerMath.CalculateAspect(new Vector2(720f, invalidDimension)),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(720f, 1280f, 0.5625f)]
        [TestCase(1920f, 1080f, 16f / 9f)]
        [TestCase(1f, 1f, 1f)]
        [TestCase(float.Epsilon, float.MaxValue, CameraScalerMath.kMinimumAspect)]
        [TestCase(float.MaxValue, float.Epsilon, CameraScalerMath.kMaximumAspect)]
        public void CalculateAspect_WithValidDimensions_ReturnsClampedRatio(float width, float height, float expected) {
            Assert.That(CameraScalerMath.CalculateAspect(new Vector2(width, height)),
                Is.EqualTo(expected).Within(0.0001f));
        }
    }
}
