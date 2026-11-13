using System.Collections.Generic;
using Frostline.Flight.Core;
using Frostline.Remote;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Frostline.Controls
{
    /// <summary>
    /// Einziges Eingabe-Modul des Spiels. Liest einen FPV-Sender (USB-Joystick, z. B. EdgeTX),
    /// ein Gamepad oder notfalls die Tastatur und liefert immer dieselbe Struktur an die
    /// Flugphysik. Die iPhone-App wird später hier als weitere Quelle eingehängt.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class StickInput : MonoBehaviour
    {
        public enum Source { None, Transmitter, Gamepad, Keyboard, Remote }

        [Tooltip("Empfänger für die iPhone-App. Leer = erster in der Szene.")]
        public RemoteControlServer remote;

        [Tooltip("Meldet sich die iPhone-App, wird automatisch auf sie umgeschaltet (außer ein Sender ist aktiv).")]
        public bool autoSelectRemote = true;

        [Tooltip("Flugmodus, solange kein Modus-Schalter zugeordnet ist.")]
        public FlightMode defaultMode = FlightMode.Acro;

        [Tooltip("Totzone für Gamepad-Sticks, die nie exakt in der Mitte stehen.")]
        [Range(0f, 0.2f)] public float gamepadDeadband = 0.04f;

        /// <summary>true = Eingaben gesperrt (z. B. während der Kalibrierung); Drohne bleibt entschärft.</summary>
        public bool Suspended { get; set; }

        public Source ActiveSource { get; private set; }
        public InputDevice Device { get; private set; }
        public InputProfile Profile { get; private set; }
        public ControlInput Current { get; private set; }
        public string DeviceLabel
        {
            get
            {
                if (ActiveSource == Source.Remote) return "iPhone" + (remote != null && remote.ClientName.Length > 0 ? ": " + remote.ClientName : "");
                return Device != null ? Device.displayName : SourceLabel(ActiveSource);
            }
        }

        readonly List<AxisControl> learnable = new List<AxisControl>();
        AxisControl rollAxis, pitchAxis, yawAxis, throttleAxis, armControl, turtleControl, resetControl, modeControl;

        // Zustand für Quellen ohne echte Schalter (Gamepad, Tastatur).
        bool toggledArm;
        FlightMode toggledMode;
        float keyboardThrottle;
        bool previousArmKey, previousModeKey, previousResetKey;
        bool resetRequested;
        bool deviceLost;
        bool remoteWasOnline;

        void OnEnable()
        {
            if (remote == null) remote = FindFirstObjectByType<RemoteControlServer>();
            toggledMode = defaultMode;
            InputSystem.onDeviceChange += OnDeviceChange;
            SelectBestDevice();
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        void Update()
        {
            bool remoteOnline = remote != null && remote.IsConnected;
            if (autoSelectRemote && remoteOnline && ActiveSource != Source.Remote && ActiveSource != Source.Transmitter && !remoteWasOnline)
            {
                SelectRemote();
            }
            remoteWasOnline = remoteOnline;
        }

        /// <summary>Fliegt mit der iPhone-App.</summary>
        public void SelectRemote()
        {
            if (remote == null) return;
            Device = null;
            Profile = null;
            deviceLost = false;
            toggledArm = false;
            learnable.Clear();
            ActiveSource = Source.Remote;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                // Ein neu eingesteckter Sender hat Vorrang vor Gamepad und Tastatur.
                if (Device == null || (ActiveSource != Source.Transmitter && device is Joystick))
                {
                    SelectBestDevice();
                }
            }
            else if ((change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected) && device == Device)
            {
                // Kabel gezogen: wie Funkabriss behandeln, bis ein Gerät neu gewählt ist.
                deviceLost = true;
                toggledArm = false;
            }
        }

        // ------------------------------------------------------------ Geräteauswahl

        /// <summary>Alle Geräte, mit denen man fliegen kann.</summary>
        public List<InputDevice> ListDevices()
        {
            var result = new List<InputDevice>();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (!device.added) continue;
                if (device is Joystick || device is Gamepad) result.Add(device);
            }
            return result;
        }

        public void SelectBestDevice()
        {
            InputDevice best = null;
            foreach (InputDevice device in ListDevices())
            {
                if (device is Joystick) { best = device; break; }
                if (best == null) best = device;
            }
            SelectDevice(best);
        }

        /// <summary>null = Tastatur.</summary>
        public void SelectDevice(InputDevice device)
        {
            Device = device;
            deviceLost = false;
            toggledArm = false;
            learnable.Clear();

            if (device == null)
            {
                ActiveSource = Keyboard.current != null ? Source.Keyboard : Source.None;
                Profile = null;
                return;
            }

            if (device is Gamepad)
            {
                ActiveSource = Source.Gamepad;
                Profile = null;
                return;
            }

            ActiveSource = Source.Transmitter;
            foreach (InputControl control in device.allControls)
            {
                // Nur echte Achsen und Tasten, keine aus anderen abgeleiteten Hilfswerte.
                if (control is AxisControl axis && !control.synthetic) learnable.Add(axis);
            }
            Profile = InputProfile.Load(ProfileName(device));
            if (!Profile.calibrated) AssignDefaults();
            ResolveBindings();
        }

        static string ProfileName(InputDevice device)
        {
            string product = device.description.product;
            return string.IsNullOrEmpty(product) ? device.displayName : product;
        }

        static string SourceLabel(Source source)
        {
            switch (source)
            {
                case Source.Keyboard: return "Tastatur";
                case Source.Gamepad: return "Gamepad";
                case Source.Transmitter: return "Sender";
                case Source.Remote: return "iPhone";
                default: return "kein Eingabegerät";
            }
        }

        /// <summary>
        /// Ohne Kalibrierung: Kanalreihenfolge AETR auf den ersten vier Achsen und Kanal 5
        /// als Arm-Schalter. Das ist die übliche Belegung im EdgeTX-Joystick-Modus.
        /// </summary>
        void AssignDefaults()
        {
            var axes = new List<AxisControl>();
            foreach (AxisControl control in learnable)
            {
                if (!(control is ButtonControl)) axes.Add(control);
            }
            if (axes.Count < 4) return;

            Profile.roll = DefaultAxis(axes[0], false);
            Profile.pitch = DefaultAxis(axes[1], false);
            Profile.throttle = DefaultAxis(axes[2], false);
            Profile.yaw = DefaultAxis(axes[3], false);
            if (axes.Count >= 5)
            {
                Profile.arm = new SwitchBinding { path = RelativePath(axes[4]), offValue = -1f, onValue = 1f };
            }
        }

        AxisBinding DefaultAxis(AxisControl control, bool inverted)
        {
            return new AxisBinding
            {
                path = RelativePath(control),
                low = inverted ? 1f : -1f,
                center = 0f,
                high = inverted ? -1f : 1f,
            };
        }

        /// <summary>Nach Änderungen am Profil aufrufen (z. B. am Ende der Kalibrierung).</summary>
        public void ResolveBindings()
        {
            rollAxis = Find(Profile != null ? Profile.roll.path : null);
            pitchAxis = Find(Profile != null ? Profile.pitch.path : null);
            yawAxis = Find(Profile != null ? Profile.yaw.path : null);
            throttleAxis = Find(Profile != null ? Profile.throttle.path : null);
            armControl = Find(Profile != null ? Profile.arm.path : null);
            turtleControl = Find(Profile != null ? Profile.turtle.path : null);
            resetControl = Find(Profile != null ? Profile.reset.path : null);
            modeControl = Find(Profile != null ? Profile.mode.path : null);
        }

        AxisControl Find(string path)
        {
            if (Device == null || string.IsNullOrEmpty(path)) return null;
            return Device.TryGetChildControl(path) as AxisControl;
        }

        // ------------------------------------------------------------ Für die Kalibrierung

        /// <summary>Alle Achsen und Tasten des gewählten Senders.</summary>
        public IReadOnlyList<AxisControl> LearnableControls { get { return learnable; } }

        public string RelativePath(InputControl control)
        {
            string full = control.path;
            string prefix = control.device.path + "/";
            return full.StartsWith(prefix) ? full.Substring(prefix.Length) : full;
        }

        public static float ReadRaw(AxisControl control)
        {
            return control != null ? control.ReadUnprocessedValue() : 0f;
        }

        public float[] CaptureRaw()
        {
            var values = new float[learnable.Count];
            for (int i = 0; i < values.Length; i++) values[i] = ReadRaw(learnable[i]);
            return values;
        }

        /// <summary>Übernimmt ein fertig kalibriertes Profil und speichert es für dieses Gerät.</summary>
        public void ApplyProfile(InputProfile profile)
        {
            if (profile == null || Device == null) return;
            profile.deviceName = ProfileName(Device);
            profile.calibrated = true;
            Profile = profile;
            Profile.Save();
            ResolveBindings();
        }

        // ------------------------------------------------------------ Lesen

        public bool ConsumeResetRequest()
        {
            bool value = resetRequested;
            resetRequested = false;
            return value;
        }

        /// <summary>Wird von der Drohne in jedem Physik-Schritt aufgerufen.</summary>
        public ControlInput Read()
        {
            ControlInput input = default(ControlInput);
            input.Mode = defaultMode;
            input.SignalValid = true;

            if (Suspended)
            {
                toggledArm = false;
                Current = input;
                return input;
            }

            if (deviceLost)
            {
                input.SignalValid = false;
                Current = input;
                return input;
            }

            switch (ActiveSource)
            {
                case Source.Transmitter: ReadTransmitter(ref input); break;
                case Source.Gamepad: ReadGamepad(ref input); break;
                case Source.Keyboard: ReadKeyboard(ref input); break;
                case Source.Remote: ReadRemote(ref input); break;
            }

            Current = input;
            return input;
        }

        void ReadRemote(ref ControlInput input)
        {
            if (remote == null)
            {
                input.SignalValid = false;
                return;
            }
            input = remote.ReadInput();
            if (remote.ConsumeReset()) resetRequested = true;
        }

        void ReadTransmitter(ref ControlInput input)
        {
            if (Profile == null || rollAxis == null || pitchAxis == null || yawAxis == null || throttleAxis == null)
            {
                return;
            }

            float deadband = Profile.deadband;
            input.Roll = MathUtil.ApplyDeadband(Profile.roll.MapCentered(ReadRaw(rollAxis)), deadband);
            input.Pitch = MathUtil.ApplyDeadband(Profile.pitch.MapCentered(ReadRaw(pitchAxis)), deadband);
            input.Yaw = MathUtil.ApplyDeadband(Profile.yaw.MapCentered(ReadRaw(yawAxis)), deadband);
            input.Throttle = Profile.throttle.MapThrottle(ReadRaw(throttleAxis));

            if (armControl != null)
            {
                input.ArmSwitch = Profile.arm.IsOn(ReadRaw(armControl));
            }
            else
            {
                // Kein Arm-Schalter zugeordnet: Leertaste schärft.
                UpdateKeyboardToggles();
                input.ArmSwitch = toggledArm;
            }

            if (turtleControl != null) input.TurtleSwitch = Profile.turtle.IsOn(ReadRaw(turtleControl));
            if (modeControl != null)
            {
                input.Mode = Profile.mode.Resolve(ReadRaw(modeControl));
            }
            else
            {
                // Kein Modus-Schalter zugeordnet: Taste M wechselt den Modus.
                UpdateKeyboardMode();
                input.Mode = toggledMode;
            }

            if (resetControl != null)
            {
                bool pressed = Profile.reset.IsOn(ReadRaw(resetControl));
                if (pressed && !previousResetKey) resetRequested = true;
                previousResetKey = pressed;
            }
            else
            {
                UpdateKeyboardReset();
            }
        }

        void ReadGamepad(ref ControlInput input)
        {
            var pad = Device as Gamepad;
            if (pad == null) return;

            // Unverarbeitet lesen: die eingebaute Stick-Totzone wäre für FPV viel zu groß.
            Vector2 left = pad.leftStick.ReadUnprocessedValue();
            Vector2 right = pad.rightStick.ReadUnprocessedValue();

            input.Yaw = MathUtil.ApplyDeadband(Mathf.Clamp(left.x, -1f, 1f), gamepadDeadband);
            // Der Stick federt in die Mitte zurück: Mitte = Gas null, nach oben = mehr Gas.
            input.Throttle = Mathf.Clamp01(left.y);
            input.Roll = MathUtil.ApplyDeadband(Mathf.Clamp(right.x, -1f, 1f), gamepadDeadband);
            input.Pitch = MathUtil.ApplyDeadband(Mathf.Clamp(right.y, -1f, 1f), gamepadDeadband);

            Edge(pad.buttonSouth.isPressed, ref previousArmKey, ref toggledArm);
            bool modePressed = pad.buttonNorth.isPressed;
            if (modePressed && !previousModeKey) toggledMode = NextMode(toggledMode);
            previousModeKey = modePressed;
            bool resetPressed = pad.buttonWest.isPressed;
            if (resetPressed && !previousResetKey) resetRequested = true;
            previousResetKey = resetPressed;

            input.ArmSwitch = toggledArm;
            input.Mode = toggledMode;
            input.TurtleSwitch = pad.buttonEast.isPressed;
        }

        void ReadKeyboard(ref ControlInput input)
        {
            Keyboard keys = Keyboard.current;
            if (keys == null) return;

            float dt = Time.fixedDeltaTime;
            if (keys.wKey.isPressed) keyboardThrottle += 0.7f * dt;
            if (keys.sKey.isPressed) keyboardThrottle -= 0.7f * dt;
            keyboardThrottle = Mathf.Clamp01(keyboardThrottle);

            input.Throttle = keyboardThrottle;
            input.Yaw = (keys.dKey.isPressed ? 0.5f : 0f) - (keys.aKey.isPressed ? 0.5f : 0f);
            input.Roll = (keys.rightArrowKey.isPressed ? 0.6f : 0f) - (keys.leftArrowKey.isPressed ? 0.6f : 0f);
            input.Pitch = (keys.upArrowKey.isPressed ? 0.6f : 0f) - (keys.downArrowKey.isPressed ? 0.6f : 0f);

            UpdateKeyboardToggles();
            UpdateKeyboardMode();
            UpdateKeyboardReset();
            if (!toggledArm) keyboardThrottle = 0f;

            input.ArmSwitch = toggledArm;
            input.Mode = toggledMode;
            input.TurtleSwitch = keys.tKey.isPressed;
        }

        void UpdateKeyboardToggles()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null) return;
            Edge(keys.spaceKey.isPressed, ref previousArmKey, ref toggledArm);
        }

        void UpdateKeyboardMode()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null) return;
            bool pressed = keys.mKey.isPressed;
            if (pressed && !previousModeKey) toggledMode = NextMode(toggledMode);
            previousModeKey = pressed;
        }

        void UpdateKeyboardReset()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null) return;
            bool pressed = keys.rKey.isPressed;
            if (pressed && !previousResetKey) resetRequested = true;
            previousResetKey = pressed;
        }

        static void Edge(bool pressed, ref bool previous, ref bool toggle)
        {
            if (pressed && !previous) toggle = !toggle;
            previous = pressed;
        }

        static FlightMode NextMode(FlightMode mode)
        {
            switch (mode)
            {
                case FlightMode.Acro: return FlightMode.Angle;
                case FlightMode.Angle: return FlightMode.Horizon;
                default: return FlightMode.Acro;
            }
        }
    }
}
