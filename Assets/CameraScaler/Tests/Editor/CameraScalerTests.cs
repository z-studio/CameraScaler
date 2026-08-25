using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Scaler = ZStudio.CameraScaler.CameraScaler;

namespace CameraScaler.Tests {
    [TestFixture]
    public sealed class CameraScalerTests {
        private GameObject m_GameObject;
        private Camera m_Camera;
        private Scaler m_Scaler;

        [TearDown]
        public void TearDown() {
            if (m_GameObject != null) Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void ConstantWidth_Orthographic_KeepsReferenceHorizontalSize() {
            CreateScaler(true, 4f / 3f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.WorkingMode = Scaler.EWorkingMode.ConstantWidth;

            m_Scaler.Refresh();

            float expectedSize = 5f * ((1600f / 900f) / (4f / 3f));
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(expectedSize).Within(0.0001f));
        }

        [Test]
        public void CameraZoom_Perspective_UsesProjectionScaleInsteadOfDividingDegrees() {
            CreateScaler(false, 16f / 9f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.WorkingMode = Scaler.EWorkingMode.ConstantHeight;
            m_Scaler.CameraZoom = 2f;

            m_Scaler.Refresh();

            float expected = ProjectionScaleToFov(FovToProjectionScale(60f) / 2f);
            Assert.That(m_Camera.fieldOfView, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(m_Camera.fieldOfView, Is.Not.EqualTo(30f).Within(0.0001f));
        }

        [Test]
        public void MatchWidthOrHeight_Perspective_InterpolatesProjectionScale() {
            CreateScaler(false, 9f / 16f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.WorkingMode = Scaler.EWorkingMode.MatchWidthOrHeight;
            m_Scaler.MatchWidthOrHeight = 0.5f;

            m_Scaler.Refresh();

            float constantHeightScale = FovToProjectionScale(60f);
            float horizontalScale = constantHeightScale * (1600f / 900f);
            float constantWidthScale = horizontalScale / (9f / 16f);
            float expectedScale = Mathf.Sqrt(constantHeightScale * constantWidthScale);
            float expectedFov = ProjectionScaleToFov(expectedScale);
            Assert.That(m_Camera.fieldOfView, Is.EqualTo(expectedFov).Within(0.0001f));
        }

        [Test]
        public void RuntimeConfigurationChange_RefreshesInitializedCameraImmediately() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1f, 1f);
            m_Scaler.WorkingMode = Scaler.EWorkingMode.ConstantWidth;
            m_Scaler.Refresh();

            m_Scaler.ReferenceResolution = new Vector2(2f, 1f);

            Assert.That(m_Camera.orthographicSize, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void InvalidConfiguration_IsSanitizedWithoutProducingInvalidCameraValues() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(0f, float.PositiveInfinity);
            m_Scaler.MatchWidthOrHeight = float.NaN;
            m_Scaler.Refresh();

            Assert.That(m_Scaler.ReferenceResolution, Is.EqualTo(Vector2.one));
            Assert.That(m_Scaler.MatchWidthOrHeight, Is.EqualTo(0.5f));
            Assert.That(float.IsNaN(m_Camera.orthographicSize), Is.False);
            Assert.That(float.IsInfinity(m_Camera.orthographicSize), Is.False);
        }

        [Test]
        public void InvalidCameraZoom_IsRejectedAndKeepsPreviousValue() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.CameraZoom = 2f;
            LogAssert.Expect(LogType.Error, new Regex("CameraZoom 必须是大于 0 的有限值"));

            m_Scaler.CameraZoom = 0f;

            Assert.That(m_Scaler.CameraZoom, Is.EqualTo(2f));
        }

        private void CreateScaler(bool orthographic, float aspect, float size, float fov) {
            m_GameObject = new GameObject("CameraScaler Test");
            m_Camera = m_GameObject.AddComponent<Camera>();
            m_Camera.orthographic = orthographic;
            m_Camera.aspect = aspect;
            m_Camera.orthographicSize = size;
            m_Camera.fieldOfView = fov;
            m_Scaler = m_GameObject.AddComponent<Scaler>();
        }

        private static float FovToProjectionScale(float fov) {
            return Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
        }

        private static float ProjectionScaleToFov(float scale) {
            return 2f * Mathf.Atan(scale) * Mathf.Rad2Deg;
        }
    }
}

