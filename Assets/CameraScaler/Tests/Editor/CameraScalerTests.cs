using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZStudio.CameraScaler.Tests.Editor {
    [TestFixture]
    public sealed class CameraScalerTests {
        private GameObject m_GameObject;
        private Camera m_Camera;
        private CameraScaler m_Scaler;

        [TearDown]
        public void TearDown() {
            if (m_GameObject != null) Object.DestroyImmediate(m_GameObject);
        }

        [Test]
        public void ConstantWidth_Orthographic_KeepsReferenceHorizontalSize() {
            CreateScaler(true, 4f / 3f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.ScaleMode = EScaleMode.ConstantWidth;

            m_Scaler.Refresh();

            float expectedSize = CameraScalerMath.CalculateOrthographicSize(
                5f,
                1600f / 900f,
                4f / 3f,
                EScaleMode.ConstantWidth,
                0.5f,
                1f
            );
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(expectedSize).Within(0.0001f));
        }

        [Test]
        public void CameraZoom_Perspective_UsesProjectionScaleInsteadOfDividingDegrees() {
            CreateScaler(false, 16f / 9f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.ScaleMode = EScaleMode.ConstantHeight;
            m_Scaler.CameraZoom = 2f;

            m_Scaler.Refresh();

            float expected = CameraScalerMath.CalculateFieldOfView(
                60f,
                1600f / 900f,
                16f / 9f,
                EScaleMode.ConstantHeight,
                0.5f,
                2f
            );
            Assert.That(m_Camera.fieldOfView, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(m_Camera.fieldOfView, Is.Not.EqualTo(30f).Within(0.0001f));
        }

        [Test]
        public void MatchWidthOrHeight_Perspective_InterpolatesProjectionScale() {
            CreateScaler(false, 9f / 16f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.ScaleMode = EScaleMode.MatchWidthOrHeight;
            m_Scaler.MatchWidthOrHeight = 0.5f;

            m_Scaler.Refresh();

            float expectedFov = CameraScalerMath.CalculateFieldOfView(
                60f,
                1600f / 900f,
                9f / 16f,
                EScaleMode.MatchWidthOrHeight,
                0.5f,
                1f
            );
            Assert.That(m_Camera.fieldOfView, Is.EqualTo(expectedFov).Within(0.0001f));
        }

        [Test]
        public void MatchWidthOrHeight_Orthographic_InterpolatesSize() {
            CreateScaler(true, 9f / 16f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.ScaleMode = EScaleMode.MatchWidthOrHeight;
            m_Scaler.MatchWidthOrHeight = 0.5f;

            m_Scaler.Refresh();

            float expected = CameraScalerMath.CalculateOrthographicSize(
                5f,
                1600f / 900f,
                9f / 16f,
                EScaleMode.MatchWidthOrHeight,
                0.5f,
                1f
            );
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void Expand_Orthographic_KeepsReferenceAreaVisible() {
            CreateScaler(true, 16f / 9f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(900f, 1600f);
            m_Scaler.ScaleMode = EScaleMode.Expand;

            m_Scaler.Refresh();

            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void Shrink_Orthographic_AvoidsShowingOutsideReferenceArea() {
            CreateScaler(true, 16f / 9f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(900f, 1600f);
            m_Scaler.ScaleMode = EScaleMode.Shrink;

            m_Scaler.Refresh();

            float expected = CameraScalerMath.CalculateOrthographicSize(
                5f,
                900f / 1600f,
                16f / 9f,
                EScaleMode.Shrink,
                0.5f,
                1f
            );
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(m_Camera.orthographicSize, Is.LessThan(5f));
        }

        [Test]
        public void AspectChange_ConstantWidth_RecalculatesSize() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = Vector2.one;
            m_Scaler.ScaleMode = EScaleMode.ConstantWidth;
            m_Scaler.Refresh();

            m_Camera.aspect = 0.5f;
            m_Scaler.Refresh();

            Assert.That(m_Camera.orthographicSize, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void HorizontalMetrics_UseReferenceBaselineWithoutZoom() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(720f, 1280f);
            m_Scaler.CameraZoom = 2f;
            m_Scaler.Refresh();

            float expectedAspect = 720f / 1280f;
            Assert.That(m_Scaler.HorizontalSize, Is.EqualTo(5f * expectedAspect).Within(0.0001f));
            Assert.That(
                m_Scaler.HorizontalFov,
                Is.EqualTo(CameraScalerMath.CalcHorizontalFov(60f, expectedAspect)).Within(0.0001f)
            );
        }

        [Test]
        public void RecaptureBaseline_UsesCurrentCameraValuesAsNewReference() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = Vector2.one;
            m_Scaler.ScaleMode = EScaleMode.ConstantHeight;
            m_Scaler.Refresh();

            m_Camera.orthographicSize = 10f;
            m_Scaler.RecaptureBaseline();
            m_Scaler.Refresh();

            Assert.That(m_Scaler.ReferenceOrthographicSize, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void RuntimeConfigurationChange_RefreshesInitializedCameraImmediately() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = new Vector2(1f, 1f);
            m_Scaler.ScaleMode = EScaleMode.ConstantWidth;
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

        [Test]
        public void InvalidWorkingMode_IsRejectedAndKeepsPreviousValue() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ScaleMode = EScaleMode.Expand;
            LogAssert.Expect(LogType.Error, new Regex("无效的 CameraScaler 工作模式"));

            m_Scaler.ScaleMode = (EScaleMode)999;

            Assert.That(m_Scaler.ScaleMode, Is.EqualTo(EScaleMode.Expand));
        }

        [Test]
        public void ApplyTiming_CanBeChanged() {
            CreateScaler(true, 1f, 5f, 60f);

            m_Scaler.ApplyTiming = EApplyTiming.OnPreCull;

            Assert.That(m_Scaler.ApplyTiming, Is.EqualTo(EApplyTiming.OnPreCull));
        }

        [Test]
        public void InvalidApplyTiming_IsRejectedAndKeepsPreviousValue() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ApplyTiming = EApplyTiming.LateUpdate;
            LogAssert.Expect(LogType.Error, new Regex("无效的 CameraScaler 写入时机"));

            m_Scaler.ApplyTiming = (EApplyTiming)999;

            Assert.That(m_Scaler.ApplyTiming, Is.EqualTo(EApplyTiming.LateUpdate));
        }

        [Test]
        public void SwitchingProjectionType_AppliesMatchingBaseline() {
            CreateScaler(true, 16f / 9f, 5f, 50f);
            m_Scaler.ReferenceResolution = new Vector2(1600f, 900f);
            m_Scaler.ScaleMode = EScaleMode.ConstantHeight;
            m_Scaler.Refresh();

            m_Camera.orthographic = false;
            m_Scaler.Refresh();

            float expected = CameraScalerMath.CalculateFieldOfView(
                50f,
                1600f / 900f,
                16f / 9f,
                EScaleMode.ConstantHeight,
                0.5f,
                1f
            );
            Assert.That(m_Camera.fieldOfView, Is.EqualTo(expected).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator EditPreview_DefaultDisabled_DoesNotTrackAspect() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = Vector2.one;
            Assert.That(m_Scaler.PreviewInEditMode, Is.False);
            m_Camera.aspect = 0.5f;

            yield return null;
            yield return null;

            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator EditPreview_TracksAspectProjectionAndSerializedSettings() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = Vector2.one;
            m_Scaler.ApplyTiming = EApplyTiming.OnPreCull;
            m_Scaler.PreviewInEditMode = true;
            m_Camera.aspect = 0.5f;

            yield return null;
            yield return null;

            Assert.That(m_Camera.orthographicSize, Is.EqualTo(10f).Within(0.0001f));
            m_Camera.orthographic = false;
            yield return null;
            yield return null;

            Assert.That(m_Camera.fieldOfView,
                Is.EqualTo(Camera.HorizontalToVerticalFieldOfView(60f, 0.5f)).Within(0.0001f));

            var serialized = new UnityEditor.SerializedObject(m_Scaler);
            serialized.FindProperty("m_ReferenceFieldOfView").floatValue = 90f;
            serialized.ApplyModifiedProperties();
            yield return null;
            yield return null;

            Assert.That(m_Camera.fieldOfView,
                Is.EqualTo(Camera.HorizontalToVerticalFieldOfView(90f, 0.5f)).Within(0.0001f));
            m_Scaler.PreviewInEditMode = false;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(m_Camera.fieldOfView, Is.EqualTo(60f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator EditPreview_DisableAndReenable_RestoresAndResumes() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = Vector2.one;
            m_Scaler.PreviewInEditMode = true;
            m_Scaler.CameraZoom = 2f;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));

            m_Scaler.enabled = false;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
            m_Camera.aspect = 0.5f;
            m_Scaler.CameraZoom = 4f;
            yield return null;
            yield return null;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));

            m_Scaler.enabled = true;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));
            m_Scaler.PreviewInEditMode = false;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator EditPreview_Recapture_DoesNotCaptureAdaptedValues() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ReferenceResolution = Vector2.one;
            m_Scaler.PreviewInEditMode = true;
            m_Scaler.CameraZoom = 2f;
            m_Scaler.RecaptureBaseline();
            yield return null;
            yield return null;

            Assert.That(m_Scaler.ReferenceOrthographicSize, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));
            Object.DestroyImmediate(m_Scaler);
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator EditPreview_SaveScene_PreservesOriginalCameraValues() {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string path = "Assets/CameraScalerPreviewTest-" + System.Guid.NewGuid().ToString("N") + ".unity";
            try {
                CreateScaler(true, 1f, 5f, 60f);
                m_Scaler.ScaleMode = EScaleMode.ConstantHeight;
                m_Camera.orthographicSize = 7f;
                m_Scaler.PreviewInEditMode = true;
                m_Scaler.CameraZoom = 2f;
                Assert.That(m_Camera.orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));

                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path, true), Is.True);
                Assert.That(System.IO.File.ReadAllText(path), Does.Contain("orthographic size: 7"));
                yield return null;
                yield return null;
                Assert.That(m_Camera.orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));
                m_Scaler.PreviewInEditMode = false;
                Assert.That(m_Camera.orthographicSize, Is.EqualTo(7f).Within(0.0001f));
            } finally {
                UnityEditor.AssetDatabase.DeleteAsset(path);
            }
        }

        [UnityTest]
        public IEnumerator EditPreview_PlayModeRoundTrip_PreservesBaselineAndOriginalCamera() {
            CreateScaler(true, 1f, 5f, 60f);
            m_Scaler.ScaleMode = EScaleMode.ConstantHeight;
            m_Camera.orthographicSize = 7f;
            m_Scaler.PreviewInEditMode = true;
            m_Scaler.CameraZoom = 2f;

            yield return new UnityEngine.TestTools.EnterPlayMode();
            var playingScaler = GameObject.Find("CameraScaler Test").GetComponent<CameraScaler>();
            Assert.That(playingScaler.GetComponent<Camera>().orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(playingScaler.ReferenceOrthographicSize, Is.EqualTo(5f).Within(0.0001f));
            playingScaler.PreviewInEditMode = false;
            Assert.That(playingScaler.GetComponent<Camera>().orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));

            yield return new UnityEngine.TestTools.ExitPlayMode();
            yield return null;
            yield return null;
            m_GameObject = GameObject.Find("CameraScaler Test");
            m_Scaler = m_GameObject.GetComponent<CameraScaler>();
            m_Camera = m_GameObject.GetComponent<Camera>();
            Assert.That(m_Scaler.PreviewInEditMode, Is.True);
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(2.5f).Within(0.0001f));
            m_Scaler.PreviewInEditMode = false;
            Assert.That(m_Camera.orthographicSize, Is.EqualTo(7f).Within(0.0001f));
        }

        private void CreateScaler(bool orthographic, float aspect, float size, float fov) {
            m_GameObject = new GameObject("CameraScaler Test");
            m_Camera = m_GameObject.AddComponent<Camera>();
            m_Camera.orthographic = orthographic;
            m_Camera.aspect = aspect;
            m_Camera.orthographicSize = size;
            m_Camera.fieldOfView = fov;
            m_Scaler = m_GameObject.AddComponent<CameraScaler>();
            m_Camera.aspect = aspect;
            m_Scaler.ReferenceOrthographicSize = size;
            m_Scaler.ReferenceFieldOfView = fov;
            m_Scaler.Refresh();
        }
    }
}
