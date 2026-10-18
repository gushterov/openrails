// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.IO;
using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using ORTS.Common;

namespace Orts.Simulation.RollingStocks.SubSystems.Brakes
{
    /// <summary>
    /// Low-speed pneumatic holding brake, released by the main traction lever.
    /// Its pressure demand is independent of the driver's engine-brake controller.
    /// </summary>
    public class AutomaticRetainingBrake
    {
        public bool Enabled { get; set; }
        public float ApplySpeedMpS { get; set; } = MpS.FromKpH(2);
        public float DepartureSpeedMpS { get; set; } = MpS.FromKpH(3);
        public float DepartureTimeoutS { get; set; } = 8;
        public float PressurePSI { get; set; } = Bar.ToPSI(2);
        public bool Applied { get; private set; }

        bool DeparturePermitted;
        bool DepartureTimerRunning;
        float DepartureElapsedS;
        ControllerPosition PreviousPosition = ControllerPosition.Undefined;
        bool PressureInitialized;
        float DriverPressurePSI;
        float AutomaticPressurePSI;

        public AutomaticRetainingBrake() { }

        public AutomaticRetainingBrake(AutomaticRetainingBrake other)
        {
            Enabled = other.Enabled;
            ApplySpeedMpS = other.ApplySpeedMpS;
            DepartureSpeedMpS = other.DepartureSpeedMpS;
            DepartureTimeoutS = other.DepartureTimeoutS;
            PressurePSI = other.PressurePSI;
        }

        public void Update(float elapsedClockSeconds, float speedMpS, ControllerPosition position, bool doorsClosed = true)
        {
            float speed = Math.Abs(speedMpS);
            bool enable = position == ControllerPosition.ThrottleIncrease
                || position == ControllerPosition.ThrottleIncreaseFast;
            bool previouslyEnable = PreviousPosition == ControllerPosition.ThrottleIncrease
                || PreviousPosition == ControllerPosition.ThrottleIncreaseFast;

            if (doorsClosed && enable && !previouslyEnable && !DeparturePermitted)
            {
                DeparturePermitted = true;
                DepartureTimerRunning = false;
                DepartureElapsedS = 0;
            }

            if (DeparturePermitted && !DepartureTimerRunning && position == ControllerPosition.Drive)
                DepartureTimerRunning = true;

            // Reaching the departure speed ends the temporary low-speed exemption.
            // Neutral, unclosed doors and a failed departure revoke it too, even above
            // the apply speed. A blocked ENABLE must be selected again after closing.
            if (speed >= DepartureSpeedMpS || position == ControllerPosition.Neutral || !doorsClosed)
            {
                DeparturePermitted = false;
                DepartureTimerRunning = false;
            }
            else if (DepartureTimerRunning)
            {
                DepartureElapsedS += Math.Max(0, elapsedClockSeconds);
                if (DepartureElapsedS >= DepartureTimeoutS)
                {
                    DeparturePermitted = false;
                    DepartureTimerRunning = false;
                }
            }

            Applied = Enabled && speed < ApplySpeedMpS && !DeparturePermitted;
            PreviousPosition = position;
        }

        public void UpdateEngineBrakePressure(ref float pressurePSI, ScriptedBrakeController controller,
            float elapsedClockSeconds, float applyRatePSIpS, float releaseRatePSIpS)
        {
            if (!PressureInitialized)
            {
                DriverPressurePSI = pressurePSI;
                PressureInitialized = true;
            }

            // Keep automatic pressure out of the controller's input. In particular, a
            // lapped manual brake must not retain pressure added by the retaining brake.
            controller.UpdateEngineBrakePressure(ref DriverPressurePSI, elapsedClockSeconds);
            float maximumPressurePSI = Math.Max(0, controller.MaxPressurePSI - controller.FullServReductionPSI);
            float targetPressurePSI = Applied ? Math.Min(PressurePSI, maximumPressurePSI) : 0;
            float elapsed = Math.Max(0, elapsedClockSeconds);
            if (AutomaticPressurePSI < targetPressurePSI)
                AutomaticPressurePSI = Math.Min(targetPressurePSI, AutomaticPressurePSI + Math.Max(0, applyRatePSIpS) * elapsed);
            else
                AutomaticPressurePSI = Math.Max(targetPressurePSI, AutomaticPressurePSI - Math.Max(0, releaseRatePSIpS) * elapsed);
            pressurePSI = Math.Max(DriverPressurePSI, AutomaticPressurePSI);
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Applied);
            writer.Write(DeparturePermitted);
            writer.Write(DepartureTimerRunning);
            writer.Write(DepartureElapsedS);
            writer.Write((int)PreviousPosition);
            writer.Write(PressureInitialized);
            writer.Write(DriverPressurePSI);
            writer.Write(AutomaticPressurePSI);
        }

        public void CopyPressureStateFrom(AutomaticRetainingBrake other)
        {
            // The shared pipe already contains the old cab's automatic demand.
            // Do not mistake that pressure for a manual application in the new cab.
            PressureInitialized = other.PressureInitialized;
            DriverPressurePSI = other.DriverPressurePSI;
            AutomaticPressurePSI = other.AutomaticPressurePSI;
            DeparturePermitted = false;
            DepartureTimerRunning = false;
            DepartureElapsedS = 0;
            PreviousPosition = ControllerPosition.Undefined;
        }

        public void Restore(BinaryReader reader)
        {
            Applied = reader.ReadBoolean();
            DeparturePermitted = reader.ReadBoolean();
            DepartureTimerRunning = reader.ReadBoolean();
            DepartureElapsedS = reader.ReadSingle();
            PreviousPosition = (ControllerPosition)reader.ReadInt32();
            PressureInitialized = reader.ReadBoolean();
            DriverPressurePSI = reader.ReadSingle();
            AutomaticPressurePSI = reader.ReadSingle();
        }
    }
}
