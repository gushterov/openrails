// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation.Signalling;
using Orts.Simulation;
using Orts.Simulation.Physics;
using Xunit;
using SignalAspectValue = Orts.Formats.Msts.MstsSignalAspect;

namespace Tests.Orts.Simulation
{
    public class SignalDispatcherTests
    {
        static SignalObject CreateSignal(MstsSignalAspect automaticAspect, bool csScript)
        {
            var signals = (Signals)FormatterServices.GetUninitializedObject(typeof(Signals));
            typeof(Signals).GetField(nameof(Signals.SignalFunctions)).SetValue(signals,
                new Dictionary<string, SignalFunction> { { "NORMAL", SignalFunction.NORMAL } });
            var signal = new SignalObject(signals, SignalObjectType.Signal);
            var head = (SignalHead)FormatterServices.GetUninitializedObject(typeof(SignalHead));
            head.mainSignal = signal;
            typeof(SignalHead).GetProperty(nameof(SignalHead.Function)).SetValue(head, SignalFunction.NORMAL);
            head.signalType = new SignalType(SignalFunction.NORMAL, MstsSignalAspect.CLEAR_2);
            head.signalType.DrawStates.Add("STOP", new SignalDrawState("STOP", 0));
            head.signalType.DrawStates.Add("APPROACH", new SignalDrawState("APPROACH", 2));
            head.signalType.Aspects.Add(new SignalAspect(MstsSignalAspect.STOP, "STOP"));
            head.signalType.Aspects.Add(new SignalAspect(MstsSignalAspect.APPROACH_1, "APPROACH"));
            signal.SignalHeads.Add(head);
            typeof(SignalObject).GetField("internalBlockState", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(signal, automaticAspect == MstsSignalAspect.STOP
                    ? SignalObject.InternalBlockstate.Blocked : SignalObject.InternalBlockstate.Reservable);
            Signals.scrfile = new SIGSCRfile(null);
            if (csScript)
                head.usedCsSignalScript = new EventSignalScript(head, automaticAspect);
            return signal;
        }

        [Theory]
        [InlineData(HoldState.ManualLock, MstsSignalAspect.STOP, false)]
        [InlineData(HoldState.ManualApproach, MstsSignalAspect.APPROACH_1, false)]
        [InlineData(HoldState.ManualPass, MstsSignalAspect.CLEAR_2, false)]
        [InlineData(HoldState.ManualLock, MstsSignalAspect.STOP, true)]
        [InlineData(HoldState.ManualApproach, MstsSignalAspect.APPROACH_1, true)]
        [InlineData(HoldState.ManualPass, MstsSignalAspect.CLEAR_2, true)]
        public void DispatcherAspectSurvivesUpdatesUntilReleased(HoldState hold, MstsSignalAspect expected, bool csScript)
        {
            var automaticAspect = expected == MstsSignalAspect.STOP ? MstsSignalAspect.CLEAR_2 : MstsSignalAspect.STOP;
            var previousScriptFile = Signals.scrfile;
            try
            {
                var signal = CreateSignal(automaticAspect, csScript);
                switch (hold)
                {
                    case HoldState.ManualLock: signal.RequestMostRestrictiveAspect(); break;
                    case HoldState.ManualApproach: signal.RequestApproachAspect(); break;
                    case HoldState.ManualPass: signal.RequestLeastRestrictiveAspect(); break;
                }

                for (int i = 0; i < 5; i++)
                {
                    // Route checks can also update the aspects between regular signal updates.
                    signal.StateUpdate();
                    AssertAspect(signal, expected);
                    signal.Update();
                    AssertAspect(signal, expected);
                }

                signal.ClearHoldSignalDispatcher();
                signal.StateUpdate();
                Assert.Equal(HoldState.None, signal.holdState);
                AssertAspect(signal, automaticAspect);
            }
            finally { Signals.scrfile = previousScriptFile; }
        }

        static void AssertAspect(SignalObject signal, MstsSignalAspect expected)
        {
            var head = Assert.Single(signal.SignalHeads);
            Assert.Equal(expected, head.state);
            Assert.Equal(head.def_draw_state(expected), head.draw_state);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StationHoldStillUpdatesTheRouteScript(bool csScript)
        {
            var previousScriptFile = Signals.scrfile;
            try
            {
                var signal = CreateSignal(MstsSignalAspect.STOP, csScript);
                signal.SignalHeads[0].state = MstsSignalAspect.CLEAR_2;
                signal.holdState = HoldState.StationStop;
                signal.StateUpdate();
                AssertAspect(signal, MstsSignalAspect.STOP);
            }
            finally { Signals.scrfile = previousScriptFile; }
        }

        [Theory]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.Reserved, SignalObject.Permission.Granted)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.Reservable, SignalObject.Permission.Granted)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.OccupiedSameDirection, SignalObject.Permission.Granted)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.ReservedOther, SignalObject.Permission.Denied)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.OccupiedOppositeDirection, SignalObject.Permission.Denied)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.Blocked, SignalObject.Permission.Denied)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, SignalObject.InternalBlockstate.ForcedWait, SignalObject.Permission.Denied)]
        [InlineData(Train.TRAIN_CONTROL.MANUAL, SignalObject.InternalBlockstate.Reserved, SignalObject.Permission.Granted)]
        [InlineData(Train.TRAIN_CONTROL.AUTO_SIGNAL, SignalObject.InternalBlockstate.Reserved, SignalObject.Permission.Denied)]
        public void PermissionAtScriptedRedRespectsRouteAvailability(Train.TRAIN_CONTROL mode,
            SignalObject.InternalBlockstate blockState, SignalObject.Permission expected)
        {
            var previousScriptFile = Signals.scrfile;
            var previousFunctions = SignalConfigurationFile.MstsSignalFunctions;
            try
            {
                SignalConfigurationFile.MstsSignalFunctions = new Dictionary<MstsSignalFunction, SignalFunction>
                {
                    { MstsSignalFunction.NORMAL, SignalFunction.NORMAL }
                };
                var signal = CreateSignal(MstsSignalAspect.STOP, true);
                var simulator = (Simulator)FormatterServices.GetUninitializedObject(typeof(Simulator));
                typeof(Signals).GetField("Simulator", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(signal.signalRef, simulator);
                var train = (Train)FormatterServices.GetUninitializedObject(typeof(Train));
                train.ControlMode = mode;
                train.HoldingSignals = new List<int>();
                train.NextSignalObject = new[] { signal, null };
                train.routedForward = new Train.TrainRouted(train, 0);
                signal.enabledTrain = train.routedForward;
                signal.hasPermission = SignalObject.Permission.Requested;
                typeof(SignalObject).GetField("internalBlockState", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(signal, blockState);

                signal.checkRouteState(false, signal.signalRoute, train.routedForward, false);

                Assert.Equal(expected, signal.hasPermission);
                // Permission authorizes passing the script's red; it does not force a green aspect.
                AssertAspect(signal, MstsSignalAspect.STOP);
            }
            finally
            {
                Signals.scrfile = previousScriptFile;
                SignalConfigurationFile.MstsSignalFunctions = previousFunctions;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitExplorerRequestReachesRedBeyondAutomaticClearAhead(bool requestPermission)
        {
            var previousScriptFile = Signals.scrfile;
            var previousFunctions = SignalConfigurationFile.MstsSignalFunctions;
            try
            {
                SignalConfigurationFile.MstsSignalFunctions = new Dictionary<MstsSignalFunction, SignalFunction>
                {
                    { MstsSignalFunction.NORMAL, SignalFunction.NORMAL }
                };
                var first = CreateSignal(MstsSignalAspect.CLEAR_2, true);
                first.SignalHeads[0].state = MstsSignalAspect.CLEAR_2;
                first.SignalNumClearAhead_ORTS = 1;
                first.SignalNumClearAheadActive = 1;
                var red = CreateSignal(MstsSignalAspect.STOP, true);
                typeof(SignalObject).GetField(nameof(SignalObject.signalRef)).SetValue(red, first.signalRef);
                red.thisRef = 1;
                red.SignalNumClearAhead_ORTS = 0;
                red.SignalNumClearAheadActive = 0;
                var signals = first.signalRef;
                typeof(Signals).GetProperty(nameof(Signals.SignalObjects)).SetValue(signals, new[] { first, red });
                var simulator = (Simulator)FormatterServices.GetUninitializedObject(typeof(Simulator));
                typeof(Signals).GetField("Simulator", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(signals, simulator);
                var train = (Train)FormatterServices.GetUninitializedObject(typeof(Train));
                typeof(Train).GetProperty(nameof(Train.Simulator)).SetValue(train, simulator);
                typeof(Train).GetProperty(nameof(Train.signalRef)).SetValue(train, signals);
                train.ControlMode = Train.TRAIN_CONTROL.EXPLORER;
                train.HoldingSignals = new List<int>();
                train.NextSignalObject = new[] { first, null };
                train.PresentPosition = new[] { new Train.TCPosition(), new Train.TCPosition() };
                train.PresentPosition[0].TCSectionIndex = 1;
                train.PresentPosition[0].RouteListIndex = 0;
                train.PresentPosition[1].RouteListIndex = 0;
                train.LastReservedSection = new int[2];
                train.DeadlockInfo = new Dictionary<int, List<Dictionary<int, int>>>();
                train.routedForward = new Train.TrainRouted(train, 0);
                train.routedBackward = new Train.TrainRouted(train, 1);
                var route = new Train.TCSubpathRoute { new Train.TCRouteElement(1, 0), new Train.TCRouteElement(2, 0) };
                train.ValidRoute = new[] { route, new Train.TCSubpathRoute() };
                first.enabledTrain = train.routedForward;
                first.TCReference = 1;
                red.TCReference = 2;
                red.hasFixedRoute = true;
                var fixedRoute = new Train.TCSubpathRoute { new Train.TCRouteElement(3, 0) };
                typeof(SignalObject).GetField("fixedRoute", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(red, fixedRoute);
                signals.TrackCircuitList = new List<TrackCircuitSection> { null };
                for (int i = 1; i <= 3; i++)
                    signals.TrackCircuitList.Add(new TrackCircuitSection(i, signals)
                    {
                        Length = 100, CircuitType = TrackCircuitSection.TrackCircuitType.EndOfTrack
                    });
                signals.TrackCircuitList[1].EndSignals[0] = first;
                signals.TrackCircuitList[2].EndSignals[0] = red;
                red.hasPermission = requestPermission ? SignalObject.Permission.Requested : SignalObject.Permission.Denied;
                var authority = Train.END_AUTHORITY.SIGNAL;
                float distance = 0;

                train.CheckExplorerPath(0, train.PresentPosition[0], route, true, ref authority, ref distance);

                Assert.Equal(requestPermission ? SignalObject.Permission.Granted : SignalObject.Permission.Denied, red.hasPermission);
                Assert.Equal(requestPermission, red.enabledTrain != null);
            }
            finally
            {
                Signals.scrfile = previousScriptFile;
                SignalConfigurationFile.MstsSignalFunctions = previousFunctions;
            }
        }

        // Like a route script that handles a dispatcher event for one update, then resumes
        // deriving its aspect from the route. The simulator must preserve the manual hold.
        sealed class EventSignalScript : CsSignalScript
        {
            readonly SignalHead head;
            readonly MstsSignalAspect automaticAspect;
            MstsSignalAspect? requestedAspect;

            public EventSignalScript(SignalHead head, MstsSignalAspect automaticAspect)
            {
                this.head = head;
                this.automaticAspect = automaticAspect;
            }

            public override void Initialize() { }
            public override void HandleEvent(SignalEvent evt, string message = "")
            {
                requestedAspect = evt == SignalEvent.RequestMostRestrictiveAspect ? SignalAspectValue.STOP
                    : evt == SignalEvent.RequestApproachAspect ? SignalAspectValue.APPROACH_1 : SignalAspectValue.CLEAR_2;
            }

            public override void Update()
            {
                head.state = requestedAspect ?? automaticAspect;
                head.draw_state = head.def_draw_state(head.state);
                requestedAspect = null;
            }
        }
    }
}
