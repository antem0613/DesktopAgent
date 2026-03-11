#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace uDesktopMascot.Editor.EditorTest
{
    public class SettingsTests
    {
        // --- CharacterSettings のテスト ---
        [Test]
        public void CharacterSettings_DefaultValues()
        {
            var settings = new CharacterSettings();
            Assert.AreEqual("default.vrm", settings.ModelPath);
            Assert.AreEqual(3.0f, settings.Scale);
            Assert.AreEqual(0.0f, settings.PositionX);
            Assert.AreEqual(0.0f, settings.PositionY);
            Assert.AreEqual(0.0f, settings.PositionZ);
            Assert.AreEqual(0.0f, settings.RotationX);
            Assert.AreEqual(0.0f, settings.RotationY);
            Assert.AreEqual(0.0f, settings.RotationZ);
        }

        [Test]
        public void CharacterSettings_Scale_Clamp()
        {
            var settings = new CharacterSettings();

            settings.Scale = 5.0f;
            Assert.AreEqual(5.0f, settings.Scale);

            settings.Scale = 0.05f;
            Assert.AreEqual(0.1f, settings.Scale);

            settings.Scale = 15.0f;
            Assert.AreEqual(10.0f, settings.Scale);
        }

        [Test]
        public void CharacterSettings_Position_Clamp()
        {
            var settings = new CharacterSettings();

            settings.PositionX = -150.0f;
            Assert.AreEqual(-100.0f, settings.PositionX);

            settings.PositionY = 50.0f;
            Assert.AreEqual(50.0f, settings.PositionY);

            settings.PositionZ = 150.0f;
            Assert.AreEqual(100.0f, settings.PositionZ);
        }

        [Test]
        public void CharacterSettings_Rotation_Normalization()
        {
            var settings = new CharacterSettings();

            settings.RotationY = 370.0f;
            Assert.AreEqual(10.0f, settings.RotationY);

            settings.RotationZ = -30.0f;
            Assert.AreEqual(330.0f, settings.RotationZ);
        }

        [Test]
        public void CharacterSettings_ModelPath_Setting()
        {
            var settings = new CharacterSettings();

            settings.ModelPath = "custom.vrm";
            Assert.AreEqual("custom.vrm", settings.ModelPath);

            settings.ModelPath = null;
            Assert.IsNull(settings.ModelPath);

            settings.ModelPath = "";
            Assert.AreEqual("", settings.ModelPath);
        }

        // --- SoundSettings のテスト ---
        [Test]
        public void SoundSettings_DefaultValues()
        {
            var settings = new SoundSettings();
            Assert.AreEqual(1.0f, settings.VoiceVolume);
            Assert.AreEqual(1.0f, settings.SEVolume);
        }

        [Test]
        public void SoundSettings_Volume_Clamp()
        {
            var settings = new SoundSettings();

            settings.VoiceVolume = -0.5f;
            Assert.AreEqual(0.0f, settings.VoiceVolume);

            settings.VoiceVolume = 0.8f;
            Assert.AreEqual(0.8f, settings.VoiceVolume);

            settings.VoiceVolume = 1.5f;
            Assert.AreEqual(1.0f, settings.VoiceVolume);
        }

        // --- DisplaySettings のテスト ---
        [Test]
        public void DisplaySettings_DefaultValues()
        {
            var settings = new DisplaySettings();
            Assert.AreEqual(1.0f, settings.Opacity);
            Assert.IsTrue(settings.AlwaysOnTop);
        }

        [Test]
        public void DisplaySettings_Opacity_Clamp()
        {
            var settings = new DisplaySettings();

            settings.Opacity = -0.2f;
            Assert.AreEqual(0.0f, settings.Opacity);

            settings.Opacity = 0.5f;
            Assert.AreEqual(0.5f, settings.Opacity);

            settings.Opacity = 1.2f;
            Assert.AreEqual(1.0f, settings.Opacity);
        }

        [Test]
        public void DisplaySettings_AlwaysOnTop_Setting()
        {
            var settings = new DisplaySettings();

            settings.AlwaysOnTop = false;
            Assert.IsFalse(settings.AlwaysOnTop);

            settings.AlwaysOnTop = true;
            Assert.IsTrue(settings.AlwaysOnTop);
        }

        // --- PerformanceSettings のテスト ---
        [Test]
        public void PerformanceSettings_DefaultValues()
        {
            var settings = new PerformanceSettings();
            Assert.AreEqual(60, settings.TargetFrameRate);
            Assert.AreEqual(2, settings.QualityLevel);
        }

        [Test]
        public void PerformanceSettings_TargetFrameRate_Clamp()
        {
            var settings = new PerformanceSettings();

            settings.TargetFrameRate = 10;
            Assert.AreEqual(15, settings.TargetFrameRate);

            settings.TargetFrameRate = 120;
            Assert.AreEqual(120, settings.TargetFrameRate);

            settings.TargetFrameRate = 300;
            Assert.AreEqual(240, settings.TargetFrameRate);
        }

        [Test]
        public void PerformanceSettings_QualityLevel_Clamp()
        {
            var settings = new PerformanceSettings();

            settings.QualityLevel = -1;
            Assert.AreEqual(0, settings.QualityLevel);

            settings.QualityLevel = 3;
            Assert.AreEqual(3, settings.QualityLevel);

            settings.QualityLevel = 10;
            Assert.AreEqual(5, settings.QualityLevel);
        }
    }
}
#endif