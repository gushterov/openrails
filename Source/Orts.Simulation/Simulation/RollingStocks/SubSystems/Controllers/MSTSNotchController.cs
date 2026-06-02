// COPYRIGHT 2010, 2011, 2012, 2013 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using Microsoft.Xna.Framework;
using Orts.Parsers.Msts;
using ORTS.Scripting.Api;
using System.Collections.Generic;
using System.IO;

namespace Orts.Simulation.RollingStocks.SubSystems.Controllers
{
    public class MSTSNotch {
        public float Value;
        public bool Smooth;
        public bool IsSubNotch;
        public ControllerState Type;
        public string Name;
        public MSTSNotch(float v, int s, string type, string name, STFReader stf, bool isSubNotch = false)
        {
            Value = v;
            Smooth = s == 0 ? false : true;
            IsSubNotch = isSubNotch;
            Type = ControllerState.Dummy;  // Default to a dummy controller state if no valid alternative state used
            string lower = type.ToLower();
            if (lower.StartsWith("trainbrakescontroller"))
                lower = lower.Substring(21);
            if (lower.StartsWith("enginebrakescontroller"))
                lower = lower.Substring(22);
            if (lower.StartsWith("brakemanbrakescontroller"))
                lower = lower.Substring(24);
            switch (lower)
            {
                case "dummy": break;
                case ")": break;
                case "releasestart": Type = ControllerState.Release; break;
                case "fullquickreleasestart": Type = ControllerState.FullQuickRelease; break;
                case "runningstart": Type = ControllerState.Running; break;
                case "selflapstart": Type = ControllerState.SelfLap; break;
                case "holdstart": Type = ControllerState.Hold; break;
                case "straightbrakingreleaseonstart": Type = ControllerState.StrBrkReleaseOn; break;
                case "straightbrakingreleaseoffstart": Type = ControllerState.StrBrkReleaseOff; break;
                case "straightbrakingreleasestart": Type = ControllerState.StrBrkRelease; break;
                case "straightbrakinglapstart": Type = ControllerState.StrBrkLap; break;
                case "straightbrakingapplystart": Type = ControllerState.StrBrkApply; break;
                case "straightbrakingapplyallstart": Type = ControllerState.StrBrkApplyAll; break;
                case "straightbrakingemergencystart": Type = ControllerState.StrBrkEmergency; break;
                case "holdlappedstart": Type = ControllerState.Lap; break;
                case "neutralhandleoffstart": Type = ControllerState.Neutral; break;
                case "graduatedselflaplimitedstart": Type = ControllerState.GSelfLap; break;
                case "graduatedselflaplimitedholdingstart": Type = ControllerState.GSelfLapH; break;
                case "applystart": Type = ControllerState.Apply; break;
                case "continuousservicestart": Type = ControllerState.ContServ; break;
                case "suppressionstart": Type = ControllerState.Suppression; break;
                case "fullservicestart": Type = ControllerState.FullServ; break;
                case "emergencystart": Type = ControllerState.Emergency; break;
                case "minimalreductionstart": Type = ControllerState.MinimalReduction; break;
                case "epapplystart": Type = ControllerState.EPApply; break;
                case "eponlystart": Type = ControllerState.EPOnly; break;
                case "epfullservicestart": Type = ControllerState.EPFullServ; break;
                case "epholdstart": Type = ControllerState.SelfLap; break;
                case "smeholdstart": Type = ControllerState.SMESelfLap; break;
                case "smeonlystart": Type = ControllerState.SMEOnly; break;
                case "smefullservicestart": Type = ControllerState.SMEFullServ; break;
                case "smereleasestart": Type = ControllerState.SMEReleaseStart; break;
                case "vacuumcontinuousservicestart": Type = ControllerState.VacContServ; break;
                case "vacuumapplycontinuousservicestart": Type = ControllerState.VacApplyContServ; break;
                case "manualbrakingstart": Type = ControllerState.ManualBraking; break;
                case "brakenotchstart": Type = ControllerState.BrakeNotch; break;
                case "overchargestart": Type = ControllerState.Overcharge; break;
                case "slowservicestart": Type = ControllerState.SlowService; break;
                case "holdenginestart": Type = ControllerState.HoldEngine; break;
                case "bailoffstart": Type = ControllerState.BailOff; break;
                default:
                    STFException.TraceInformation(stf, "Skipped unknown notch type " + type);
                    break;
            }
            Name = name;
        }
        public MSTSNotch(float v, bool s, int t, bool isSubNotch = false)
        {
            Value = v;
            Smooth = s;
            IsSubNotch = isSubNotch;
            Type = (ControllerState)t;
        }

        public MSTSNotch(MSTSNotch other)
        {
            Value = other.Value;
            Smooth = other.Smooth;
            IsSubNotch = other.IsSubNotch;
            Type = other.Type;
            Name = other.Name;
        }

        public MSTSNotch Clone()
        {
            return new MSTSNotch(this);
        }

        public string GetName()
        {
            if (!string.IsNullOrEmpty(Name)) return Name;
            return ControllerStateDictionary.Dict[Type];
        }
    }

    /**
     * This is the most used controller. The main use is for diesel locomotives' Throttle control.
     * 
     * It is used with single keypress, this means that when the user press a key, only the keydown event is handled.
     * The user need to press the key multiple times to update this controller.
     * 
     */
    public class MSTSNotchController: IController
    {
        private float currentValue;
        public float CurrentValue
        {
            get
            {
                return currentValue;
            }
            set
            {
                if (currentValue == value) return;
                currentValue = value;
                TimeSinceLastChange = 0;
            }
        }
        private float savedValue;
        public float SavedValue
        {
            get
            {
                if (TimeSinceLastChange >= GetDelayTimeBeforeUpdating())
                    savedValue = currentValue;
                return savedValue;
            }
        }
        public float IntermediateValue;
        public float MinimumValue;
        public float MaximumValue = 1;
        public const float StandardBoost = 5.0f; // standard step size multiplier
        public const float FastBoost = 20.0f;
        public float StepSize;
        private List<MSTSNotch> Notches = new List<MSTSNotch>();
        public int CurrentNotch { get; set; }
        public bool ToZero = false; // true if controller zero command;

        private float OldValue;

        //Does not need to persist
        //this indicates if the controller is increasing or decreasing, 0 no changes
        public float UpdateValue { get; set; }
        private float? controllerTarget;
        public double CommandStartTime { get; set; }
        private float prevValue;
        public float TimeSinceLastChange { get; private set; }
        public float DelayTimeBeforeUpdating;
        public float DelayTimeBeforeUpdatingFromZero = -1;
        public float AutoSubNotchIncrementIntervalS;
        public bool InstantSetToZeroOnZeroCommand;
        public bool DisableSubNotchDecrease;
        public float DisableSubNotchDecreaseBelow = float.NaN;
        private const float ZeroThreshold = 0.0001f;
        private int autoSubNotchTargetNotch = -1;
        private int autoSubNotchDirection;
        private float autoSubNotchElapsedS;
        private bool autoSubNotchIgnoresStop;
        private int manualDisplayTargetNotch = -1;

        #region CONSTRUCTORS

        public MSTSNotchController()
        {
        }

        public MSTSNotchController(int numOfNotches)
        {
            MinimumValue = 0;
            MaximumValue = numOfNotches - 1;
            StepSize = 1;
            for (int i = 0; i < numOfNotches; i++)
                Notches.Add(new MSTSNotch(i, false, 0));
        }

        public MSTSNotchController(float min, float max, float stepSize)
        {
            MinimumValue = min;
            MaximumValue = max;
            StepSize = stepSize;
        }

        public MSTSNotchController(MSTSNotchController other)
        {
            CurrentValue = other.CurrentValue;
            IntermediateValue = other.IntermediateValue;
            MinimumValue = other.MinimumValue;
            MaximumValue = other.MaximumValue;
            StepSize = other.StepSize;
            CurrentNotch = other.CurrentNotch;
            DelayTimeBeforeUpdating = other.DelayTimeBeforeUpdating;
            DelayTimeBeforeUpdatingFromZero = other.DelayTimeBeforeUpdatingFromZero;
            AutoSubNotchIncrementIntervalS = other.AutoSubNotchIncrementIntervalS;
            InstantSetToZeroOnZeroCommand = other.InstantSetToZeroOnZeroCommand;
            DisableSubNotchDecrease = other.DisableSubNotchDecrease;
            DisableSubNotchDecreaseBelow = other.DisableSubNotchDecreaseBelow;

            foreach (MSTSNotch notch in other.Notches)
            {
                Notches.Add(notch.Clone());
            }
        }

        public MSTSNotchController(STFReader stf)
        {
            Parse(stf);
        }

        public MSTSNotchController(List<MSTSNotch> notches)
        {
            Notches = notches;
        }
        #endregion

        public virtual IController Clone()
        {
            return new MSTSNotchController(this);
        }

        public virtual bool IsValid()
        {
            return StepSize != 0;
        }

        public void Parse(STFReader stf)
        {
            stf.MustMatch("(");
            MinimumValue = stf.ReadFloat(STFReader.UNITS.None, null);
            MaximumValue = stf.ReadFloat(STFReader.UNITS.None, null);
            StepSize = stf.ReadFloat(STFReader.UNITS.None, null);
            IntermediateValue = CurrentValue = stf.ReadFloat(STFReader.UNITS.None, null);
            stf.ParseBlock(new STFReader.TokenProcessor[] {
                new STFReader.TokenProcessor("numnotches", () =>{
                    stf.MustMatch("(");
                    stf.ReadInt(null);
                    stf.ParseBlock(new STFReader.TokenProcessor[] {
                        new STFReader.TokenProcessor("notch", ()=>{
                            stf.MustMatch("(");
                            float value = stf.ReadFloat(STFReader.UNITS.None, null);
                            int smooth = stf.ReadInt(null);
                            string type = stf.ReadString();
                            string name = null;
                            while(type != ")" && !stf.EndOfBlock())
                            {
                                switch (stf.ReadItem().ToLower())
                                {
                                    case "(":
                                        stf.SkipRestOfBlock();
                                        break;
                                    case "ortslabel":
                                        name = stf.ReadStringBlock(null);
                                        break;
                                }
                            }
                            Notches.Add(new MSTSNotch(value, smooth, type, name, stf));
                        }),
                        new STFReader.TokenProcessor("subnotch", ()=>{
                            stf.MustMatch("(");
                            float value = stf.ReadFloat(STFReader.UNITS.None, null);
                            int smooth = stf.ReadInt(null);
                            string type = stf.ReadString();
                            string name = null;
                            while(type != ")" && !stf.EndOfBlock())
                            {
                                switch (stf.ReadItem().ToLower())
                                {
                                    case "(":
                                        stf.SkipRestOfBlock();
                                        break;
                                    case "ortslabel":
                                        name = stf.ReadStringBlock(null);
                                        break;
                                }
                                type = stf.ReadString();
                            }
                            Notches.Add(new MSTSNotch(value, smooth, type, name, stf, true));
                        }),
                    });
                }),
                new STFReader.TokenProcessor("ortsdelaytimebeforeupdating", () =>
                {
                    DelayTimeBeforeUpdating = stf.ReadFloatBlock(STFReader.UNITS.Time, null);
                }),
                new STFReader.TokenProcessor("ortsdelaytimebeforeupdatingfromzero", () =>
                {
                    DelayTimeBeforeUpdatingFromZero = stf.ReadFloatBlock(STFReader.UNITS.Time, null);
                }),
                new STFReader.TokenProcessor("ortssubnotchincrementinterval", () =>
                {
                    AutoSubNotchIncrementIntervalS = stf.ReadFloatBlock(STFReader.UNITS.Time, null);
                }),
                new STFReader.TokenProcessor("ortsinstantsettozeroonzerocommand", () =>
                {
                    InstantSetToZeroOnZeroCommand = stf.ReadBoolBlock(false);
                }),
                new STFReader.TokenProcessor("ortsinstantzerocommand", () =>
                {
                    InstantSetToZeroOnZeroCommand = stf.ReadBoolBlock(false);
                }),
                new STFReader.TokenProcessor("ortsdisablesubnotchdecrease", () =>
                {
                    DisableSubNotchDecrease = stf.ReadBoolBlock(false);
                }),
                new STFReader.TokenProcessor("ortsnosubnotchdecrease", () =>
                {
                    DisableSubNotchDecrease = stf.ReadBoolBlock(false);
                }),
                new STFReader.TokenProcessor("ortsdisablesubnotchdecreasebelow", () =>
                {
                    DisableSubNotchDecreaseBelow = stf.ReadFloatBlock(STFReader.UNITS.None, null);
                }),
                new STFReader.TokenProcessor("ortsnosubnotchdecreasebelow", () =>
                {
                    DisableSubNotchDecreaseBelow = stf.ReadFloatBlock(STFReader.UNITS.None, null);
                }),
            });
            SetValue(CurrentValue);
        }

        private float GetDelayTimeBeforeUpdating()
        {
            if (DelayTimeBeforeUpdatingFromZero >= 0
                && savedValue <= ZeroThreshold
                && currentValue > ZeroThreshold)
            {
                return DelayTimeBeforeUpdatingFromZero;
            }

            return DelayTimeBeforeUpdating;
        }

        public int NotchCount()
        {
            return Notches.Count;
        }

        public float DisplayValue
        {
            get
            {
                if (manualDisplayTargetNotch >= 0 && manualDisplayTargetNotch < Notches.Count)
                    return Notches[manualDisplayTargetNotch].Value;
                if (autoSubNotchTargetNotch >= 0 && autoSubNotchTargetNotch < Notches.Count)
                    return Notches[autoSubNotchTargetNotch].Value;
                return CurrentValue;
            }
        }

        public bool IsAutoSubNotchTraversalActive => autoSubNotchTargetNotch >= 0 || manualDisplayTargetNotch >= 0;

        private float GetNotchBoost(float boost)
        {
            return (ToZero && ((CurrentNotch >= 0 && Notches[CurrentNotch].Smooth) || Notches.Count == 0 || 
                IntermediateValue - CurrentValue > StepSize) ? FastBoost : boost);
        }

        public void AddNotch(float value)
        {
            Notches.Add(new MSTSNotch(value, false, (int)ControllerState.Dummy));
        }

        private int GetNextNotchIndex(int start, int direction, bool includeSubNotch)
        {
            int notch = start + direction;
            while (notch >= 0 && notch < Notches.Count)
            {
                if (includeSubNotch || !Notches[notch].IsSubNotch)
                    return notch;
                notch += direction;
            }
            return start;
        }

        private void StartAutomaticSubNotchTraversal(int direction, int targetNotchIndex)
        {
            autoSubNotchDirection = direction;
            autoSubNotchTargetNotch = targetNotchIndex;
            autoSubNotchElapsedS = 0;
            autoSubNotchIgnoresStop = true;
        }

        private void StopAutomaticSubNotchTraversal(bool keepUpdateValue)
        {
            autoSubNotchDirection = 0;
            autoSubNotchTargetNotch = -1;
            autoSubNotchElapsedS = 0;
            autoSubNotchIgnoresStop = false;
            if (!keepUpdateValue)
                UpdateValue = 0;
        }

        private void ClearManualDisplayTarget()
        {
            manualDisplayTargetNotch = -1;
        }

        private bool UpdateAutomaticSubNotch(float elapsedSeconds)
        {
            if (autoSubNotchTargetNotch < 0 || AutoSubNotchIncrementIntervalS <= 0 || UpdateValue == 0)
                return false;

            if (CurrentNotch == autoSubNotchTargetNotch)
            {
                StopAutomaticSubNotchTraversal(false);
                return true;
            }

            autoSubNotchElapsedS += elapsedSeconds;
            while (autoSubNotchElapsedS >= AutoSubNotchIncrementIntervalS && autoSubNotchTargetNotch >= 0)
            {
                autoSubNotchElapsedS -= AutoSubNotchIncrementIntervalS;
                int nextNotch = GetNextNotchIndex(CurrentNotch, autoSubNotchDirection, true);
                if (nextNotch == CurrentNotch)
                {
                    StopAutomaticSubNotchTraversal(false);
                    break;
                }
                CurrentNotch = nextNotch;
                IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                if (CurrentNotch == autoSubNotchTargetNotch)
                    StopAutomaticSubNotchTraversal(false);
            }
            return true;
        }

        private bool TryExtendAutomaticSubNotchTraversal(int direction)
        {
            if (AutoSubNotchIncrementIntervalS <= 0 || autoSubNotchTargetNotch < 0 || autoSubNotchDirection != direction)
                return false;

            int nextTarget = GetNextNotchIndex(autoSubNotchTargetNotch, direction, false);
            if (nextTarget == autoSubNotchTargetNotch)
                return true;

            // For instant-zero throttles: if an additional decrease command extends the queued target
            // to zero, apply zero immediately instead of waiting for the remaining sub-notch countdown.
            if (InstantSetToZeroOnZeroCommand
                && direction < 0
                && Notches[nextTarget].Value <= MinimumValue + ZeroThreshold)
            {
                StopAutomaticSubNotchTraversal(false);
                controllerTarget = null;
                ToZero = true;
                SetValue(MinimumValue);
                return true;
            }

            autoSubNotchTargetNotch = nextTarget;
            return true;
        }

        private bool IsSubNotchDecreaseDisabledForTarget(int targetNotchIndex)
        {
            if (!DisableSubNotchDecrease
                || targetNotchIndex < 0
                || targetNotchIndex >= Notches.Count)
            {
                return false;
            }

            if (float.IsNaN(DisableSubNotchDecreaseBelow))
                return true;

            return Notches[targetNotchIndex].Value < DisableSubNotchDecreaseBelow - ZeroThreshold;
        }

        /// <summary>
        /// Sets the actual value of the controller, and adjusts the actual notch to match.
        /// </summary>
        /// <param name="value">Normalized value the controller to be set to. Normally is within range [-1..1]</param>
        /// <returns>1 or -1 if there was a significant change in controller position, otherwise 0.
        /// Needed for hinting whether a serializable command is to be issued for repeatability.
        /// Sign is indicating the direction of change, being displayed by confirmer text.</returns>
        public int SetValue(float value)
        {
            // Direct value assignments (mouse/combined handle/script) override any pending auto traversal.
            StopAutomaticSubNotchTraversal(false);
            ClearManualDisplayTarget();
            controllerTarget = null;
            ToZero = false;
            if (InstantSetToZeroOnZeroCommand && value <= MinimumValue + ZeroThreshold)
                value = MinimumValue;

            CurrentValue = IntermediateValue = MathHelper.Clamp(value, MinimumValue, MaximumValue);
            var oldNotch = CurrentNotch;

            for (CurrentNotch = Notches.Count - 1; CurrentNotch > 0; CurrentNotch--)
            {
                if (Notches[CurrentNotch].Value <= CurrentValue)
                    break;
            }

            if (CurrentNotch >= 0 && !Notches[CurrentNotch].Smooth)
                CurrentValue = Notches[CurrentNotch].Value;

            var change = CurrentNotch > oldNotch || CurrentValue > OldValue + 0.1f || CurrentValue == 1 && OldValue < 1 
                ? 1 : CurrentNotch < oldNotch || CurrentValue < OldValue - 0.1f || CurrentValue == 0 && OldValue > 0 ? -1 : 0;
            if (change != 0)
                OldValue = CurrentValue;

            return change;
        }

        public float SetPercent(float percent)
        {
            // Direct value assignments (e.g. analog controls) override pending auto traversal.
            StopAutomaticSubNotchTraversal(false);
            ClearManualDisplayTarget();
            controllerTarget = null;
            ToZero = false;

            if (percent > 100) SetValue(1);
            float v = (MinimumValue < 0 && percent < 0 ? -MinimumValue : MaximumValue) * percent / 100;
            CurrentValue = MathHelper.Clamp(v, MinimumValue, MaximumValue);

            if (CurrentNotch >= 0)
            {
                if (Notches[Notches.Count - 1].Type == ControllerState.Emergency)
                    v = Notches[Notches.Count - 1].Value * percent / 100;
                for (; ; )
                {
                    MSTSNotch notch = Notches[CurrentNotch];
                    if (CurrentNotch > 0 && v < notch.Value)
                    {
                        MSTSNotch prev = Notches[CurrentNotch-1];
                        if (!notch.Smooth && !prev.Smooth && v - prev.Value > .45 * (notch.Value - prev.Value))
                            break;
                        CurrentNotch--;
                        continue;
                    }
                    if (CurrentNotch < Notches.Count - 1)
                    {
                        MSTSNotch next = Notches[CurrentNotch + 1];
                        if (next.Type != ControllerState.Emergency)
                        {
                            if ((notch.Smooth || next.Smooth) && v < next.Value)
                                break;
                            if (!notch.Smooth && !next.Smooth && v - notch.Value < .55 * (next.Value - notch.Value))
                                break;
                            CurrentNotch++;
                            continue;
                        }
                    }
                    break;
                }
                if (Notches[CurrentNotch].Smooth)
                    CurrentValue = v;
                else
                    CurrentValue = Notches[CurrentNotch].Value;
            }
            IntermediateValue = CurrentValue;
            return 100 * CurrentValue;
        }

        public void StartIncrease( float? target ) {
            controllerTarget = target;
            ToZero = false;
            StartIncrease();
        }

        public void StartIncrease()
        {
            UpdateValue = 1;

            // If a sub-notch auto-traversal in the same direction is already active,
            // pressing again should queue the next main notch instead of waiting.
            if (TryExtendAutomaticSubNotchTraversal(1))
                return;

            bool hadManualDisplayTarget = manualDisplayTargetNotch >= 0 && Notches.Count > 0;
            if (hadManualDisplayTarget)
            {
                int nextDisplayTargetNotch = GetNextNotchIndex(manualDisplayTargetNotch, 1, false);
                if (nextDisplayTargetNotch == manualDisplayTargetNotch)
                {
                    // If already at top displayed notch and actual value is also at/above it,
                    // manual display mode is complete.
                    if (Notches[manualDisplayTargetNotch].Value <= CurrentValue + ZeroThreshold)
                    {
                        ClearManualDisplayTarget();
                        hadManualDisplayTarget = false;
                    }
                }

                // While actual value is already above this displayed target step,
                // advance only the displayed notch and keep actual value unchanged.
                if (manualDisplayTargetNotch >= 0
                    && nextDisplayTargetNotch != manualDisplayTargetNotch
                    && Notches[nextDisplayTargetNotch].Value <= CurrentValue + ZeroThreshold)
                {
                    manualDisplayTargetNotch = nextDisplayTargetNotch;
                    CurrentNotch = manualDisplayTargetNotch;
                    UpdateValue = 0;
                    return;
                }
            }

            if (hadManualDisplayTarget && Notches.Count > 0)
            {
                // In display-hold mode, CurrentNotch may represent displayed target
                // instead of actual power value. Re-align to actual value before
                // starting real increase to avoid restarting from a lower notch.
                CurrentNotch = GetNotch(CurrentValue);
                IntermediateValue = CurrentValue;
            }

            int commandReferenceNotch = manualDisplayTargetNotch >= 0
                ? manualDisplayTargetNotch
                : (autoSubNotchTargetNotch >= 0 ? autoSubNotchTargetNotch : CurrentNotch);
            ClearManualDisplayTarget();
            StopAutomaticSubNotchTraversal(true);

            // When we have notches and the current Notch does not require smooth, we go directly to the next notch
            if ((Notches.Count > 0) && (CurrentNotch < Notches.Count - 1) && (!Notches[CurrentNotch].Smooth))
            {
                int targetNotch = GetNextNotchIndex(commandReferenceNotch, 1, false);
                int directionToTarget = System.Math.Sign(targetNotch - CurrentNotch);
                int nextNotch = GetNextNotchIndex(CurrentNotch, directionToTarget, true);
                if (directionToTarget != 0)
                {
                    UpdateValue = directionToTarget;
                    if (AutoSubNotchIncrementIntervalS > 0
                        && ((directionToTarget > 0 && nextNotch > CurrentNotch && nextNotch < targetNotch)
                        || (directionToTarget < 0 && nextNotch < CurrentNotch && nextNotch > targetNotch)))
                    {
                        CurrentNotch = nextNotch;
                        IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                        StartAutomaticSubNotchTraversal(directionToTarget, targetNotch);
                    }
                    else
                    {
                        if (directionToTarget < 0)
                        {
                            IntermediateValue = Notches[CurrentNotch].Value;
                            CurrentNotch = targetNotch;
                            CurrentValue = Notches[CurrentNotch].Value;
                        }
                        else
                        {
                            CurrentNotch = targetNotch;
                            IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                        }
                    }
                }
            }
		}

        public void StopIncrease()
        {
            if (autoSubNotchTargetNotch >= 0 && autoSubNotchIgnoresStop)
                return;
            UpdateValue = 0;
            StopAutomaticSubNotchTraversal(true);
        }

        public void StartDecrease( float? target, bool toZero = false)
        {
            controllerTarget = target;
            ToZero = toZero
                || (InstantSetToZeroOnZeroCommand
                && target != null
                && target <= MinimumValue + ZeroThreshold);
            StartDecrease();
        }
        
        public void StartDecrease()
        {
            if (ToZero && InstantSetToZeroOnZeroCommand)
            {
                ClearManualDisplayTarget();
                UpdateValue = 0;
                controllerTarget = null;
                StopAutomaticSubNotchTraversal(false);
                SetValue(MinimumValue);
                return;
            }

            if (!ToZero
                && Notches.Count > 0
                && CurrentNotch >= 0
                && autoSubNotchTargetNotch >= 0
                && autoSubNotchDirection > 0)
            {
                // While auto-increasing, a decrease command should retarget to the
                // previous main notch. If that new target is still above current
                // value, keep increasing until it is reached (independent of flags).
                int referenceNotch = autoSubNotchTargetNotch;
                int retargetNotch = GetNextNotchIndex(referenceNotch, -1, false);
                if (retargetNotch != referenceNotch
                    && Notches[retargetNotch].Value > CurrentValue + ZeroThreshold)
                {
                    ClearManualDisplayTarget();
                    controllerTarget = null;
                    ToZero = false;
                    autoSubNotchTargetNotch = retargetNotch;
                    UpdateValue = 1;
                    return;
                }
            }

            if (!ToZero && DisableSubNotchDecrease && Notches.Count > 0 && CurrentNotch >= 0)
            {
                // Profiles without sub-notch decrease keep actual throttle value while
                // decrease commands only move the displayed/main target notch.
                int referenceNotch = manualDisplayTargetNotch >= 0
                    ? manualDisplayTargetNotch
                    : (autoSubNotchTargetNotch >= 0 ? autoSubNotchTargetNotch : CurrentNotch);
                int nextDisplayTargetNotch = GetNextNotchIndex(referenceNotch, -1, false);
                if (nextDisplayTargetNotch != referenceNotch
                    && !IsSubNotchDecreaseDisabledForTarget(nextDisplayTargetNotch))
                {
                    ClearManualDisplayTarget();
                }
                else
                {
                    bool targetIsAboveCurrentValue = nextDisplayTargetNotch != referenceNotch
                        && Notches[nextDisplayTargetNotch].Value > CurrentValue + ZeroThreshold;

                    // Reaching minimum notch should still be possible.
                    if (nextDisplayTargetNotch != referenceNotch
                        && Notches[nextDisplayTargetNotch].Value <= MinimumValue + ZeroThreshold)
                    {
                        ClearManualDisplayTarget();
                        StopAutomaticSubNotchTraversal(false);
                        SetValue(MinimumValue);
                        return;
                    }

                    controllerTarget = null;
                    ToZero = false;

                    if (targetIsAboveCurrentValue)
                    {
                        // New requested main notch is still above current value: keep increasing
                        // until the new target is reached.
                        ClearManualDisplayTarget();
                        if (autoSubNotchTargetNotch >= 0 && autoSubNotchDirection > 0 && UpdateValue > 0)
                        {
                            autoSubNotchTargetNotch = nextDisplayTargetNotch;
                            return;
                        }

                        StopAutomaticSubNotchTraversal(false);
                        int directionToTarget = System.Math.Sign(nextDisplayTargetNotch - CurrentNotch);
                        if (directionToTarget > 0)
                        {
                            UpdateValue = directionToTarget;
                            int nextNotch = GetNextNotchIndex(CurrentNotch, directionToTarget, true);
                            if (AutoSubNotchIncrementIntervalS > 0
                                && nextNotch > CurrentNotch
                                && nextNotch < nextDisplayTargetNotch)
                            {
                                CurrentNotch = nextNotch;
                                IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                                StartAutomaticSubNotchTraversal(directionToTarget, nextDisplayTargetNotch);
                            }
                            else
                            {
                                CurrentNotch = nextDisplayTargetNotch;
                                IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                            }
                            return;
                        }
                    }

                    StopAutomaticSubNotchTraversal(false);
                    if (nextDisplayTargetNotch != referenceNotch)
                    {
                        manualDisplayTargetNotch = nextDisplayTargetNotch;
                        CurrentNotch = manualDisplayTargetNotch;
                    }

                    UpdateValue = 0;
                    return;
                }
            }

            ClearManualDisplayTarget();

            UpdateValue = -1;

            // If a sub-notch auto-traversal in the same direction is already active,
            // pressing again should queue the next main notch instead of waiting.
            if (TryExtendAutomaticSubNotchTraversal(-1))
                return;

            int commandReferenceNotch = autoSubNotchTargetNotch >= 0 ? autoSubNotchTargetNotch : CurrentNotch;
            StopAutomaticSubNotchTraversal(true);

            //If we have notches and the previous Notch does not require smooth, we go directly to the previous notch
            if ((Notches.Count > 0) && (CurrentNotch > 0) && SmoothMin() == null)
            {
                int targetNotch = GetNextNotchIndex(commandReferenceNotch, -1, false);
                int directionToTarget = System.Math.Sign(targetNotch - CurrentNotch);
                int nextNotch = GetNextNotchIndex(CurrentNotch, directionToTarget, true);
                if (directionToTarget != 0)
                {
                    UpdateValue = directionToTarget;
                    if (AutoSubNotchIncrementIntervalS > 0
                        && !(directionToTarget < 0 && IsSubNotchDecreaseDisabledForTarget(targetNotch))
                        && ((directionToTarget > 0 && nextNotch > CurrentNotch && nextNotch < targetNotch)
                        || (directionToTarget < 0 && nextNotch < CurrentNotch && nextNotch > targetNotch)))
                    {
                        CurrentNotch = nextNotch;
                        IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                        StartAutomaticSubNotchTraversal(directionToTarget, targetNotch);
                    }
                    else
                    {
                        if (directionToTarget < 0)
                        {
                            IntermediateValue = Notches[CurrentNotch].Value;
                            CurrentNotch = targetNotch;
                            CurrentValue = Notches[CurrentNotch].Value;
                        }
                        else
                        {
                            CurrentNotch = targetNotch;
                            IntermediateValue = CurrentValue = Notches[CurrentNotch].Value;
                        }
                    }
                }
            }
        }

        public void StopDecrease()
        {
            if (autoSubNotchTargetNotch >= 0 && autoSubNotchIgnoresStop)
                return;
            UpdateValue = 0;
            StopAutomaticSubNotchTraversal(true);
        }

        public float Update(float elapsedSeconds)
        {
            if (UpdateAutomaticSubNotch(elapsedSeconds))
            {
                if (prevValue == CurrentValue) TimeSinceLastChange += elapsedSeconds;
                prevValue = CurrentValue;
                return CurrentValue;
            }

            if (UpdateValue == 1 || UpdateValue == -1)
            {
                CheckControllerTargetAchieved();
                UpdateValues(elapsedSeconds, UpdateValue, StandardBoost);
            }
            if (prevValue == CurrentValue) TimeSinceLastChange += elapsedSeconds;
            prevValue = CurrentValue;
            return CurrentValue;
        }

        public float UpdateAndSetBoost(float elapsedSeconds, float boost)
        {
            if (UpdateValue == 1 || UpdateValue == -1)
            {
                CheckControllerTargetAchieved();
                UpdateValues(elapsedSeconds, UpdateValue, boost);
            }
            if (prevValue == CurrentValue) TimeSinceLastChange += elapsedSeconds;
            prevValue = CurrentValue;
            return CurrentValue;
        }

        /// <summary>
        /// If a target has been set, then stop once it's reached and also cancel the target.
        /// </summary>
        public void CheckControllerTargetAchieved() {
            if( controllerTarget != null )
            {
                if( UpdateValue > 0.0 )
                {
                    if( CurrentValue >= controllerTarget )
                    {
                        StopIncrease();
                        controllerTarget = null;
                    }
                }
                else
                {
                    if( CurrentValue <= controllerTarget )
                    {
                        StopDecrease();
                        controllerTarget = null;
                    }
                }
            }
        }

        private float UpdateValues(float elapsedSeconds, float direction, float boost)
        {
            //We increment the intermediate value first
            IntermediateValue += StepSize * elapsedSeconds * GetNotchBoost(boost) * direction;
            IntermediateValue = MathHelper.Clamp(IntermediateValue, MinimumValue, MaximumValue);

            //Do we have notches
            if (Notches.Count > 0)
            {
                //Increasing, check if the notch has changed
                if ((direction > 0) && (CurrentNotch < Notches.Count - 1) && (IntermediateValue >= Notches[CurrentNotch + 1].Value))
                {
                    // steamer_ctn - The following code was added in relation to reported bug  #1200226. However it seems to prevent the brake controller from ever being moved to EMERGENCY position.
                    // Bug conditions indicated in the bug report have not been able to be duplicated, ie there doesn't appear to be a "safety stop" when brake key(s) held down continuously
                    // Code has been reverted pending further investigation or reports of other issues
                    // Prevent TrainBrake to continuously switch to emergency
                    //      if (Notches[CurrentNotch + 1].Type == ControllerState.Emergency)
                    //         IntermediateValue = Notches[CurrentNotch + 1].Value - StepSize;
                    //      else
                    CurrentNotch++;
                }
                //decreasing, again check if the current notch has changed
                else if((direction < 0) && (CurrentNotch > 0) && (IntermediateValue < Notches[CurrentNotch].Value))
                {
                    CurrentNotch--;
                }

                //If the notch is smooth, we use intermediate value that is being update smooth thought the frames
                if (Notches[CurrentNotch].Smooth)
                    CurrentValue = IntermediateValue;
                else
                    CurrentValue = Notches[CurrentNotch].Value;
            }
            else
            {
                //if no notches, we just keep updating the current value directly
                CurrentValue = IntermediateValue;
            }
            return CurrentValue;
        }

        public float GetNotchFraction()
        {
            if (Notches.Count == 0)
                return 0;
            MSTSNotch notch = Notches[CurrentNotch];
            if (!notch.Smooth)
                // Respect British 3-wire EP brake configurations
                return (notch.Type == ControllerState.EPApply || notch.Type == ControllerState.EPOnly) ? CurrentValue : 1;
            float x = 1;
            if (CurrentNotch + 1 < Notches.Count)
                x = Notches[CurrentNotch + 1].Value;
            x = (CurrentValue - notch.Value) / (x - notch.Value);
            if (notch.Type == ControllerState.Release)
                x = 1 - x;
            return x;
        }

        public float? SmoothMin()
        {
            float? target = null;
            if (Notches.Count > 0)
            {
                if (CurrentNotch > 0 && Notches[CurrentNotch - 1].Smooth)
                    target = Notches[CurrentNotch - 1].Value;
                else if (Notches[CurrentNotch].Smooth && CurrentValue > Notches[CurrentNotch].Value)
                    target = Notches[CurrentNotch].Value;
            }
            else
                target = MinimumValue;
            return target;
        }

        public float? SmoothMax()
        {
            float? target = null;
            if (Notches.Count > 0 && CurrentNotch < Notches.Count - 1 && Notches[CurrentNotch].Smooth)
                target = Notches[CurrentNotch + 1].Value;
            else if (Notches.Count == 0
                || (Notches.Count == 1 && Notches[CurrentNotch].Smooth))
                target = MaximumValue;
            return target;
        }

        public float? DPSmoothMax()
        {
            float? target = null;
            if (Notches.Count > 0 && CurrentNotch < Notches.Count - 1 && Notches[CurrentNotch].Smooth)
                target = Notches[CurrentNotch + 1].Value;
            else if (Notches.Count == 0 || CurrentNotch == Notches.Count - 1 && Notches[CurrentNotch].Smooth)
                target = MaximumValue;
            return target;
        }

        public virtual string GetStatus()
        {
            if (Notches.Count == 0)
                return string.Format("{0:F0}%", 100 * CurrentValue);
            MSTSNotch notch = Notches[CurrentNotch];
            if (!notch.Smooth && notch.Type == ControllerState.Dummy)
                return string.Format("{0:F0}%", 100 * CurrentValue);
            if (!notch.Smooth)
                return notch.GetName();
            if (notch.GetName().Length > 0)
                return string.Format("{0} {1:F0}%", notch.GetName(), 100 * GetNotchFraction());
            return string.Format("{0:F0}%", 100 * GetNotchFraction());
        }

        public virtual void Save(BinaryWriter outf)
        {
            outf.Write((int)ControllerTypes.MSTSNotchController);

            this.SaveData(outf);
        }

        protected virtual void SaveData(BinaryWriter outf)
        {            
            outf.Write(CurrentValue);            
            outf.Write(MinimumValue);
            outf.Write(MaximumValue);
            outf.Write(StepSize);
            outf.Write(CurrentNotch);           
        }

        public virtual void Restore(BinaryReader inf)
        {
            IntermediateValue = CurrentValue = inf.ReadSingle();            
            MinimumValue = inf.ReadSingle();
            MaximumValue = inf.ReadSingle();
            StepSize = inf.ReadSingle();
            CurrentNotch = inf.ReadInt32();

            UpdateValue = 0;         
        }

        public MSTSNotch GetCurrentNotch()
        {
            return Notches.Count == 0 ? null : Notches[CurrentNotch];
        }

        protected void SetCurrentNotch(ControllerState type)
        {
            for (int i = 0; i < Notches.Count; i++)
            {
                if (Notches[i].Type == type)
                {
                    CurrentNotch = i;
                    CurrentValue = Notches[i].Value;

                    break;
                }
            }
        }

        public void SetStepSize ( float stepSize)
        {
            StepSize = stepSize;
        }

        public void Normalize (float ratio)
        {
            for (int i = 0; i < Notches.Count; i++)
                Notches[i].Value /= ratio;
        }

        /// <summary>
        /// Get the nearest discrete notch position for a normalized input value.
        /// This function is not dependent on notch controller actual (current) value, so can be queried for computer-intervened value as well.
        /// </summary>
        public int GetNearestNotch(float value)
        {
            var notch = 0;
            for (notch = Notches.Count - 1; notch > 0; notch--)
            {
                if (Notches[notch].Value <= value)
                {
                    if (notch < Notches.Count - 1 && Notches[notch + 1].Value - value < value - Notches[notch].Value)
                        notch++;
                    break;
                }
            }
            return notch;
        }

        /// <summary>
        /// Get the discrete notch position for a normalized input value.
        /// This function is not dependent on notch controller actual (current) value, so can be queried for computer-intervened value as well.
        /// </summary>
        public int GetNotch(float value)
        {
            var notch = 0;
            for (notch = Notches.Count - 1; notch > 0; notch--)
            {
                if (Notches[notch].Value <= value)
                {
                     break;
                }
            }
            return notch;
        }

        public float GetFirstMainNotchAboveMinimumValue()
        {
            for (int i = 0; i < Notches.Count; i++)
            {
                if (!Notches[i].IsSubNotch && Notches[i].Value > MinimumValue + ZeroThreshold)
                    return Notches[i].Value;
            }
            return MinimumValue;
        }

    }
}
