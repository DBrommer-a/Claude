using Frostline.Flight.Core;
using NUnit.Framework;

namespace Frostline.Tests
{
    public class RatesTests
    {
        [Test]
        public void Betaflight_DefaultRates_Give667DegPerSecond()
        {
            // Bekannter Referenzwert: RC Rate 1.0, Super Rate 0.7 -> 667 Grad/s.
            Assert.That(Rates.Betaflight(1f, 1.0f, 0.70f, 0f), Is.EqualTo(666.67f).Within(0.01f));
            Assert.That(Rates.Betaflight(-1f, 1.0f, 0.70f, 0f), Is.EqualTo(-666.67f).Within(0.01f));
        }

        [Test]
        public void Betaflight_HalfStick_MatchesFormula()
        {
            // 200 * 1.0 * 0.5 / (1 - 0.5 * 0.7)
            Assert.That(Rates.Betaflight(0.5f, 1.0f, 0.70f, 0f), Is.EqualTo(153.846f).Within(0.01f));
            // mit Expo 0.5: Stick wird zu 0.5 * 0.125 * 0.5 + 0.5 * 0.5 = 0.28125
            Assert.That(Rates.Betaflight(0.5f, 1.0f, 0.70f, 0.5f), Is.EqualTo(86.538f).Within(0.01f));
        }

        [Test]
        public void Betaflight_RcRateAboveTwo_GetsSteeper()
        {
            // rcRate 2.2 -> 2.2 + 14.54 * 0.2 = 5.108 -> 1021.6 Grad/s
            Assert.That(Rates.Betaflight(1f, 2.2f, 0f, 0f), Is.EqualTo(1021.6f).Within(0.1f));
        }

        [Test]
        public void Actual_FullStick_IsMaxRate()
        {
            Assert.That(Rates.Actual(1f, 70f, 670f, 0f), Is.EqualTo(670f).Within(0.01f));
            Assert.That(Rates.Actual(1f, 70f, 670f, 0.6f), Is.EqualTo(670f).Within(0.01f));
            Assert.That(Rates.Actual(-1f, 70f, 670f, 0.6f), Is.EqualTo(-670f).Within(0.01f));
        }

        [Test]
        public void Actual_HalfStick_MatchesFormula()
        {
            // 0.5 * 70 + 600 * 0.5 * 0.5
            Assert.That(Rates.Actual(0.5f, 70f, 670f, 0f), Is.EqualTo(185f).Within(0.01f));
            // Expo 0.5: 0.5 * (0.5^5 * 0.5 + 0.5 * 0.5) = 0.1328125
            Assert.That(Rates.Actual(0.5f, 70f, 670f, 0.5f), Is.EqualTo(114.6875f).Within(0.01f));
        }

        [Test]
        public void Rates_AreZeroAtCenter_AndSymmetric()
        {
            Assert.That(Rates.Betaflight(0f, 1.2f, 0.75f, 0.3f), Is.EqualTo(0f));
            Assert.That(Rates.Actual(0f, 70f, 670f, 0.3f), Is.EqualTo(0f));
            for (float s = 0.1f; s <= 1f; s += 0.1f)
            {
                Assert.That(Rates.Betaflight(-s, 1.2f, 0.75f, 0.3f), Is.EqualTo(-Rates.Betaflight(s, 1.2f, 0.75f, 0.3f)).Within(1e-3f));
                Assert.That(Rates.Actual(-s, 70f, 670f, 0.3f), Is.EqualTo(-Rates.Actual(s, 70f, 670f, 0.3f)).Within(1e-3f));
            }
        }

        [Test]
        public void Rates_NeverExceedLimit()
        {
            Assert.That(Rates.Betaflight(1f, 2.55f, 0.99f, 0f), Is.LessThanOrEqualTo(Rates.RateLimit));
            Assert.That(Rates.Actual(1f, 500f, 5000f, 0f), Is.LessThanOrEqualTo(Rates.RateLimit));
        }

        [Test]
        public void Profile_SwitchesBetweenTypes()
        {
            var profile = new RateProfile();
            profile.Type = RateType.Betaflight;
            Assert.That(profile.MaxRate(0), Is.EqualTo(666.67f).Within(0.01f));
            profile.Type = RateType.Actual;
            Assert.That(profile.MaxRate(0), Is.EqualTo(670f).Within(0.01f));
        }

        [Test]
        public void Deadband_RemovesCenterAndKeepsFullRange()
        {
            Assert.That(MathUtil.ApplyDeadband(0.02f, 0.03f), Is.EqualTo(0f));
            Assert.That(MathUtil.ApplyDeadband(1f, 0.03f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(MathUtil.ApplyDeadband(-1f, 0.03f), Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(MathUtil.ApplyDeadband(0.5f, 0.1f), Is.EqualTo(0.4444f).Within(1e-3f));
        }
    }
}
