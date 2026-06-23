// COPYRIGHT 2026 by the Open Rails project.
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

using Orts.Simulation.RollingStocks.SubSystems.Controllers;
using System.Collections.Generic;
using Xunit;

namespace Tests.Orts.Simulation
{
    public static class MSTSNotchControllerTests
    {
        private static MSTSNotchController CreateSubNotchedThrottle()
        {
            var controller = new MSTSNotchController(new List<MSTSNotch>
            {
                new MSTSNotch(0.00f, false, 0),
                new MSTSNotch(0.05f, false, 0),
                new MSTSNotch(0.10f, false, 0, true),
                new MSTSNotch(0.15f, false, 0, true),
                new MSTSNotch(0.20f, false, 0, true),
                new MSTSNotch(0.25f, false, 0, true),
                new MSTSNotch(0.30f, false, 0, true),
                new MSTSNotch(0.35f, false, 0, true),
                new MSTSNotch(0.40f, false, 0, true),
                new MSTSNotch(0.45f, false, 0, true),
                new MSTSNotch(0.50f, false, 0),
                new MSTSNotch(0.55f, false, 0, true),
                new MSTSNotch(0.60f, false, 0, true),
                new MSTSNotch(0.65f, false, 0, true),
                new MSTSNotch(0.70f, false, 0, true),
                new MSTSNotch(0.75f, false, 0, true),
                new MSTSNotch(0.80f, false, 0),
                new MSTSNotch(0.90f, false, 0),
                new MSTSNotch(0.99f, false, 0),
            })
            {
                MinimumValue = 0,
                MaximumValue = 1,
                StepSize = 0.025f,
                AutoSubNotchIncrementIntervalS = 1,
            };

            controller.SetValue(0.05f);
            return controller;
        }

        private static MSTSNotchController CreateSubNotchedThrottleWithAdjacentMainNotches()
        {
            var controller = new MSTSNotchController(new List<MSTSNotch>
            {
                new MSTSNotch(0.00f, false, 0),
                new MSTSNotch(0.04f, false, 0),
                new MSTSNotch(0.10f, false, 0),
                new MSTSNotch(0.16f, false, 0),
                new MSTSNotch(0.18f, false, 0),
                new MSTSNotch(0.20f, false, 0, true),
                new MSTSNotch(0.22f, false, 0, true),
                new MSTSNotch(0.24f, false, 0, true),
                new MSTSNotch(0.26f, false, 0, true),
                new MSTSNotch(0.28f, false, 0, true),
                new MSTSNotch(0.30f, false, 0, true),
                new MSTSNotch(0.32f, false, 0, true),
                new MSTSNotch(0.34f, false, 0, true),
                new MSTSNotch(0.36f, false, 0, true),
                new MSTSNotch(0.38f, false, 0, true),
                new MSTSNotch(0.40f, false, 0, true),
                new MSTSNotch(0.42f, false, 0, true),
                new MSTSNotch(0.44f, false, 0, true),
                new MSTSNotch(0.46f, false, 0, true),
                new MSTSNotch(0.48f, false, 0, true),
                new MSTSNotch(0.50f, false, 0, true),
                new MSTSNotch(0.52f, false, 0, true),
                new MSTSNotch(0.54f, false, 0, true),
                new MSTSNotch(0.56f, false, 0, true),
                new MSTSNotch(0.58f, false, 0, true),
                new MSTSNotch(0.60f, false, 0, true),
                new MSTSNotch(0.63f, false, 0, true),
                new MSTSNotch(0.66f, false, 0, true),
                new MSTSNotch(0.69f, false, 0, true),
                new MSTSNotch(0.72f, false, 0, true),
                new MSTSNotch(0.75f, false, 0, true),
                new MSTSNotch(0.78f, false, 0, true),
                new MSTSNotch(0.81f, false, 0, true),
                new MSTSNotch(0.84f, false, 0, true),
                new MSTSNotch(0.87f, false, 0),
                new MSTSNotch(0.90f, false, 0),
                new MSTSNotch(0.95f, false, 0),
                new MSTSNotch(1.00f, false, 0),
            })
            {
                MinimumValue = 0,
                MaximumValue = 1,
                StepSize = 0.006f,
                AutoSubNotchIncrementIntervalS = 0.6f,
                InstantSetToZeroOnZeroCommand = true,
            };

            controller.SetValue(0.18f);
            return controller;
        }

        [Fact]
        public static void DecreaseToPreviousMainNotchDuringAutoIncreaseHoldsCurrentSubNotch()
        {
            var controller = CreateSubNotchedThrottle();
            controller.StartIncrease();
            controller.Update(2);

            Assert.Equal(0.20f, controller.CurrentValue, 3);

            controller.StartDecrease();
            controller.Update(5);

            Assert.Equal(0.20f, controller.CurrentValue, 3);
            Assert.Equal(0.05f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void IncreaseAfterHeldPreviousMainNotchContinuesToOriginalTarget()
        {
            var controller = CreateSubNotchedThrottle();
            controller.StartIncrease();
            controller.Update(2);
            controller.StartDecrease();

            controller.StartIncrease();
            controller.Update(10);

            Assert.Equal(0.50f, controller.CurrentValue, 3);
            Assert.Equal(0.50f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void AutoIncreaseWithForceLimitAllowsFirstStepAboveLimit()
        {
            var controller = CreateSubNotchedThrottle();
            controller.AutoSubNotchIncrementForceLimitN = 100000;

            controller.StartIncrease();
            controller.Update(5, 100000);

            Assert.Equal(0.10f, controller.CurrentValue, 3);
            Assert.Equal(0.50f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void AutoIncreaseWithForceLimitResumesBelowLimit()
        {
            var controller = CreateSubNotchedThrottle();
            controller.AutoSubNotchIncrementForceLimitN = 100000;

            controller.StartIncrease();
            controller.Update(5, 100000);
            controller.Update(1, 99999);

            Assert.Equal(0.15f, controller.CurrentValue, 3);
            Assert.Equal(0.50f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void SecondDecreaseAfterHeldPreviousMainNotchStartsDecreasingBelowIt()
        {
            var controller = CreateSubNotchedThrottle();
            controller.StartIncrease();
            controller.Update(2);
            controller.StartDecrease();

            controller.StartDecrease();

            Assert.Equal(0.15f, controller.CurrentValue, 3);
            Assert.Equal(0.00f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void IncreaseToNextMainNotchDuringAutoDecreaseHoldsCurrentSubNotch()
        {
            var controller = CreateSubNotchedThrottle();
            controller.SetValue(0.50f);
            controller.StartDecrease();
            controller.Update(2);

            Assert.Equal(0.35f, controller.CurrentValue, 3);

            controller.StartIncrease();
            controller.Update(5);

            Assert.Equal(0.35f, controller.CurrentValue, 3);
            Assert.Equal(0.50f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void SecondIncreaseAfterHeldNextMainNotchStartsIncreasingAboveIt()
        {
            var controller = CreateSubNotchedThrottle();
            controller.SetValue(0.50f);
            controller.StartDecrease();
            controller.Update(2);
            controller.StartIncrease();

            controller.StartIncrease();

            Assert.Equal(0.40f, controller.CurrentValue, 3);
            Assert.Equal(0.80f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void DecreaseAfterHeldNextMainNotchContinuesToLowerTarget()
        {
            var controller = CreateSubNotchedThrottle();
            controller.SetValue(0.50f);
            controller.StartDecrease();
            controller.Update(2);
            controller.StartIncrease();

            controller.StartDecrease();
            controller.Update(10);

            Assert.Equal(0.05f, controller.CurrentValue, 3);
            Assert.Equal(0.05f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void DirectDecreaseBetweenAdjacentMainNotchesStopsAtTarget()
        {
            var controller = CreateSubNotchedThrottleWithAdjacentMainNotches();

            controller.StartDecrease();
            controller.Update(1);

            Assert.Equal(0.16f, controller.CurrentValue, 3);
            Assert.Equal(0.16f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void DirectIncreaseBackToAdjacentMainNotchStopsBeforeSubNotches()
        {
            var controller = CreateSubNotchedThrottleWithAdjacentMainNotches();
            controller.StartDecrease();

            controller.StartIncrease();
            controller.Update(1);

            Assert.Equal(0.18f, controller.CurrentValue, 3);
            Assert.Equal(0.18f, controller.DisplayValue, 3);
        }

        [Fact]
        public static void IncreaseBackToHeldAdjacentMainNotchStopsDecreaseImmediately()
        {
            var controller = CreateSubNotchedThrottleWithAdjacentMainNotches();
            controller.StartIncrease();
            controller.Update(3);
            controller.StartDecrease();
            controller.StartDecrease();
            controller.Update(1.2f);

            float heldValue = controller.CurrentValue;

            controller.StartIncrease();
            controller.Update(5);

            Assert.Equal(heldValue, controller.CurrentValue, 3);
            Assert.Equal(0.18f, controller.DisplayValue, 3);
        }
    }
}
