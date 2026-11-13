using System;
using System.IO;
using System.Numerics;
using Frostline.Gameplay;
using NUnit.Framework;

namespace Frostline.Tests
{
    public class GameplayTests
    {
        // ------------------------------------------------------------ Gates

        static GateShape Gate()
        {
            // 3 x 2 m, Durchflug in +z, Mitte auf 1,5 m Höhe.
            return new GateShape(new Vector3(0f, 1.5f, 10f), Quaternion.Identity, 3f, 2f);
        }

        [Test]
        public void Gate_ThroughTheOpening()
        {
            Assert.That(GateMath.Test(Gate(), new Vector3(0.5f, 1.4f, 9.8f), new Vector3(0.6f, 1.6f, 10.3f)), Is.EqualTo(GateCrossing.Through));
        }

        [Test]
        public void Gate_FastFlightIsStillDetected()
        {
            // 150 km/h bei 500 Hz: 8 cm pro Schritt. Bei 60 Hz (falls Physik einbricht): 70 cm.
            Assert.That(GateMath.Test(Gate(), new Vector3(0f, 1.5f, 9.6f), new Vector3(0f, 1.5f, 10.3f)), Is.EqualTo(GateCrossing.Through));
        }

        [Test]
        public void Gate_BackwardsIsReported()
        {
            Assert.That(GateMath.Test(Gate(), new Vector3(0f, 1.5f, 10.2f), new Vector3(0f, 1.5f, 9.9f)), Is.EqualTo(GateCrossing.Backwards));
        }

        [Test]
        public void Gate_PassingBesideIsAMiss()
        {
            Assert.That(GateMath.Test(Gate(), new Vector3(2.5f, 1.5f, 9.9f), new Vector3(2.5f, 1.5f, 10.1f)), Is.EqualTo(GateCrossing.Missed));
            Assert.That(GateMath.Test(Gate(), new Vector3(0f, 3.2f, 9.9f), new Vector3(0f, 3.2f, 10.1f)), Is.EqualTo(GateCrossing.Missed));
            Assert.That(GateMath.Test(Gate(), new Vector3(50f, 1.5f, 9.9f), new Vector3(50f, 1.5f, 10.1f)), Is.EqualTo(GateCrossing.None), "weit weg zählt gar nicht");
        }

        [Test]
        public void Gate_NoCrossingWithoutReachingThePlane()
        {
            Assert.That(GateMath.Test(Gate(), new Vector3(0f, 1.5f, 8f), new Vector3(0f, 1.5f, 9.9f)), Is.EqualTo(GateCrossing.None));
        }

        [Test]
        public void Gate_RotatedGateUsesItsOwnAxes()
        {
            Quaternion yaw90 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Math.PI / 2));
            var gate = new GateShape(new Vector3(10f, 1.5f, 0f), yaw90, 3f, 2f);
            // Nach 90° Drehung zeigt die Durchflugrichtung nach +x.
            Assert.That(GateMath.Test(gate, new Vector3(9.8f, 1.5f, 0.5f), new Vector3(10.2f, 1.5f, 0.5f)), Is.EqualTo(GateCrossing.Through));
            Assert.That(GateMath.Test(gate, new Vector3(10.2f, 1.5f, 0f), new Vector3(9.8f, 1.5f, 0f)), Is.EqualTo(GateCrossing.Backwards));
        }

        // ------------------------------------------------------------ Rennen

        [Test]
        public void Race_CountdownThenGatesInOrder()
        {
            var race = new RaceSession(3, 1);
            race.BeginCountdown(10.0);
            Assert.That(race.Phase, Is.EqualTo(RacePhase.Countdown));
            Assert.That(race.CountdownRemaining(11.0), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(race.PassGate(0, 12.0), Is.False, "vor dem Start zählt nichts");

            race.Update(13.0);
            Assert.That(race.Phase, Is.EqualTo(RacePhase.Running));
            Assert.That(race.PassGate(1, 14.0), Is.False, "Gate 2 vor Gate 1 zählt nicht");
            Assert.That(race.PassGate(0, 14.0), Is.True);
            Assert.That(race.PassGate(1, 15.5), Is.True);
            Assert.That(race.Splits[1], Is.EqualTo(2.5f).Within(1e-4f));

            float lap = 0f;
            bool best = false;
            race.LapCompleted += (time, isBest) => { lap = time; best = isBest; };
            Assert.That(race.PassGate(2, 17.25), Is.True);
            Assert.That(race.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(lap, Is.EqualTo(4.25f).Within(1e-4f));
            Assert.That(best, Is.True);
            Assert.That(race.BestLap, Is.EqualTo(4.25f).Within(1e-4f));
        }

        [Test]
        public void Race_MultipleLapsKeepBestLap()
        {
            var race = new RaceSession(2, 3, bestLap: 9f);
            race.BeginAt(0.0);
            race.PassGate(0, 2.0); race.PassGate(1, 10.0);   // 10 s, keine Bestzeit
            Assert.That(race.CurrentLap, Is.EqualTo(2));
            race.PassGate(0, 13.0); race.PassGate(1, 18.0);  // 8 s, Bestzeit
            race.PassGate(0, 20.0); race.PassGate(1, 26.5);  // 8,5 s
            Assert.That(race.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(race.LapTimes.Count, Is.EqualTo(3));
            Assert.That(race.BestLap, Is.EqualTo(8f).Within(1e-4f));
            Assert.That(race.TotalTime(30.0), Is.EqualTo(26.5f).Within(1e-4f));
        }

        // ------------------------------------------------------------ Replay

        static ReplayFrame Frame(float time, float x)
        {
            return new ReplayFrame { Time = time, Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity, Throttle = x / 10f };
        }

        [Test]
        public void Replay_InterpolatesBetweenFrames()
        {
            var replay = new ReplayBuffer(10);
            replay.Add(Frame(0f, 0f));
            replay.Add(Frame(1f, 10f));
            ReplayFrame mid = replay.Sample(0.25f);
            Assert.That(mid.Position.X, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(mid.Throttle, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(replay.Sample(-5f).Position.X, Is.EqualTo(0f));
            Assert.That(replay.Sample(5f).Position.X, Is.EqualTo(10f));
        }

        [Test]
        public void Replay_RingBufferKeepsNewestFrames()
        {
            var replay = new ReplayBuffer(4);
            for (int i = 0; i < 10; i++) replay.Add(Frame(i, i));
            Assert.That(replay.Count, Is.EqualTo(4));
            Assert.That(replay.StartTime, Is.EqualTo(6f));
            Assert.That(replay.EndTime, Is.EqualTo(9f));
            Assert.That(replay.Sample(7.5f).Position.X, Is.EqualTo(7.5f).Within(1e-4f));
        }

        [Test]
        public void Replay_ExtractAndSaveRoundTrip()
        {
            var replay = new ReplayBuffer(100);
            for (int i = 0; i < 50; i++) replay.Add(Frame(i * 0.1f, i));
            ReplayBuffer lap = replay.Extract(1f, 2f);
            Assert.That(lap.StartTime, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(lap.Duration, Is.EqualTo(1f).Within(0.11f));

            var stream = new MemoryStream();
            lap.Write(stream);
            stream.Position = 0;
            ReplayBuffer loaded = ReplayBuffer.Read(stream);
            Assert.That(loaded.Count, Is.EqualTo(lap.Count));
            Assert.That(loaded.Sample(0.5f).Position.X, Is.EqualTo(lap.Sample(0.5f).Position.X).Within(1e-4f));
        }

        [Test]
        public void Replay_RejectsForeignFiles()
        {
            var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
            Assert.Throws<InvalidDataException>(() => ReplayBuffer.Read(stream));
        }

        // ------------------------------------------------------------ Signal am Kartenrand

        [Test]
        public void Signal_CleanInsideNoisyOutsideLostBeyond()
        {
            var signal = new SignalModel { CenterX = 0f, CenterZ = 0f, CleanRadius = 100f, LostRadius = 200f, LostSeconds = 1f };
            Assert.That(signal.Update(50f, 10f, 0f, 0.1f), Is.False);
            Assert.That(signal.Noise, Is.EqualTo(0f));

            signal.Update(150f, 10f, 0f, 0.1f);
            Assert.That(signal.Noise, Is.EqualTo(0.25f).Within(1e-4f));

            bool lost = false;
            for (int i = 0; i < 9; i++) lost |= signal.Update(250f, 10f, 0f, 0.1f);
            Assert.That(lost, Is.False, "erst nach einer Sekunde ohne Signal");
            Assert.That(signal.Update(250f, 10f, 0f, 0.1f) || signal.Update(250f, 10f, 0f, 0.1f), Is.True);
            Assert.That(signal.Lost, Is.True);

            signal.Update(0f, 10f, 0f, 0.1f);
            Assert.That(signal.Lost, Is.False);
        }

        [Test]
        public void Signal_AltitudeAlsoCounts()
        {
            var signal = new SignalModel { CleanAltitude = 1000f, LostAltitude = 2000f };
            signal.Update(0f, 1500f, 0f, 0.1f);
            Assert.That(signal.Noise, Is.EqualTo(0.25f).Within(1e-4f));
        }

        // ------------------------------------------------------------ Multiplayer-Interpolation

        static DroneSnapshot Snap(double time, float x, float vx)
        {
            return new DroneSnapshot { Time = time, Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(vx, 0f, 0f) };
        }

        [Test]
        public void Interpolator_PlaysBack100MillisecondsDelayed()
        {
            var interpolator = new SnapshotInterpolator();
            // 30 Hz, konstant 10 m/s.
            for (int i = 0; i <= 30; i++) interpolator.Add(Snap(i / 30.0, i / 3f, 10f));
            DroneSnapshot s;
            Assert.That(interpolator.TrySample(1.0, out s), Is.True);
            Assert.That(s.Position.X, Is.EqualTo(9f).Within(0.01f), "bei t = 0,9 s");
        }

        [Test]
        public void Interpolator_IsSmoothAcrossSnapshots()
        {
            var interpolator = new SnapshotInterpolator();
            for (int i = 0; i <= 30; i++) interpolator.Add(Snap(i / 30.0, i / 3f, 10f));
            float previous = float.NaN;
            DroneSnapshot s;
            for (double t = 0.3; t < 1.0; t += 0.004)
            {
                interpolator.TrySample(t, out s);
                if (!float.IsNaN(previous))
                {
                    float step = s.Position.X - previous;
                    Assert.That(step, Is.EqualTo(0.04f).Within(0.002f), "gleichmäßig bei t = " + t);
                }
                previous = s.Position.X;
            }
        }

        [Test]
        public void Interpolator_ExtrapolatesOnlyBriefly()
        {
            var interpolator = new SnapshotInterpolator();
            interpolator.Add(Snap(0.0, 0f, 10f));
            interpolator.Add(Snap(0.1, 1f, 10f));
            DroneSnapshot s;
            interpolator.TrySample(0.3, out s); // Renderzeit 0,2: 0,1 s voraus
            Assert.That(s.Position.X, Is.EqualTo(2f).Within(1e-3f));
            interpolator.TrySample(5.0, out s); // weit voraus: auf 0,25 s begrenzt
            Assert.That(s.Position.X, Is.EqualTo(3.5f).Within(1e-3f));
        }

        [Test]
        public void Interpolator_IgnoresOutOfOrderPackets()
        {
            var interpolator = new SnapshotInterpolator();
            interpolator.Add(Snap(1.0, 10f, 0f));
            interpolator.Add(Snap(0.9, 99f, 0f));
            Assert.That(interpolator.Count, Is.EqualTo(1));
        }
    }
}
