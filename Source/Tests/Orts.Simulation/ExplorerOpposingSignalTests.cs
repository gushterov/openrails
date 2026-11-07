// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System.Collections.Generic;
using System.Runtime.Serialization;
using Orts.Formats.Msts;
using Orts.Simulation.Physics;
using Orts.Simulation.Signalling;
using ORTS.Common;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class ExplorerOpposingSignalTests
    {
        [Theory]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 0, Direction.Forward, 0f, true, HoldState.None, true)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 1, Direction.Reverse, 0f, true, HoldState.None, true)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 1, Direction.Forward, -5f, true, HoldState.None, true)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 0, Direction.Forward, 0f, false, HoldState.None, false)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 1, Direction.Forward, 0f, true, HoldState.None, false)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 0, Direction.Reverse, -5f, true, HoldState.None, false)]
        [InlineData(Train.TRAIN_CONTROL.EXPLORER, 0, Direction.Forward, 0f, true, HoldState.ManualPass, false)]
        [InlineData(Train.TRAIN_CONTROL.MANUAL, 0, Direction.Forward, 0f, true, HoldState.None, false)]
        [InlineData(Train.TRAIN_CONTROL.AUTO_SIGNAL, 0, Direction.Forward, 0f, true, HoldState.None, false)]
        public void LeadingExplorerSignalDoesNotConflictWithOwnAutomaticReverseClearance(Train.TRAIN_CONTROL mode,
            int routeDirection, Direction reverser, float speed, bool sameTrain, HoldState opposingHold, bool ignoreOpposing)
        {
            var originalSignals = SignalObject.signalObjects;
            try
            {
                var signals = (Signals)FormatterServices.GetUninitializedObject(typeof(Signals));
                typeof(Signals).GetField(nameof(Signals.SignalFunctions)).SetValue(signals,
                    new Dictionary<string, SignalFunction> { { "NORMAL", SignalFunction.NORMAL } });
                var train = (Train)FormatterServices.GetUninitializedObject(typeof(Train));
                train.ControlMode = mode;
                train.MUDirection = reverser;
                train.SpeedMpS = speed;
                train.routedForward = new Train.TrainRouted(train, 0);
                train.routedBackward = new Train.TrainRouted(train, 1);
                var requestingTrain = routeDirection == 0 ? train.routedForward : train.routedBackward;
                var opposingTrain = sameTrain ? train : (Train)FormatterServices.GetUninitializedObject(typeof(Train));
                var signal = new SignalObject(signals, SignalObjectType.Signal)
                {
                    thisRef = 0, TCReference = 1, TCDirection = 0, enabledTrain = requestingTrain
                };
                var opposite = new SignalObject(signals, SignalObjectType.Signal)
                {
                    thisRef = 1, TCReference = 1, TCDirection = 1,
                    enabledTrain = new Train.TrainRouted(opposingTrain, 1 - routeDirection), holdState = opposingHold
                };
                var head = (SignalHead)FormatterServices.GetUninitializedObject(typeof(SignalHead));
                typeof(SignalHead).GetProperty(nameof(SignalHead.Function)).SetValue(head, SignalFunction.NORMAL);
                head.state = MstsSignalAspect.CLEAR_1;
                opposite.SignalHeads.Add(head);
                SignalObject.signalObjects = new[] { signal, opposite };
                var section = new TrackCircuitSection(1, signals);
                section.CircuitState.TrainReserved = requestingTrain;
                section.EndSignals[1] = opposite;
                signals.TrackCircuitList = new List<TrackCircuitSection> { null, section };
                var expected = ignoreOpposing ? MstsSignalAspect.STOP : MstsSignalAspect.CLEAR_1;

                Assert.Equal(expected, signal.opp_sig_lr(SignalFunction.NORMAL));
                Assert.Equal(expected, signal.opp_sig_mr(SignalFunction.NORMAL));
                SignalObject found = null;
                Assert.Equal(expected, signal.opp_sig_lr(SignalFunction.NORMAL, ref found));
                Assert.Same(opposite, found);
                Assert.Equal(expected, signal.opp_sig_mr(SignalFunction.NORMAL, ref found));
                Assert.Same(opposite, found);
                Assert.Equal(MstsSignalAspect.CLEAR_1, head.state);
            }
            finally { SignalObject.signalObjects = originalSignals; }
        }
    }
}
