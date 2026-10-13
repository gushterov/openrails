// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Orts.Formats.Msts;

namespace Orts.Viewer3D
{
    /// <summary>Automatic triggers fire once per door opening or entry into the low-speed range.</summary>
    public sealed class CabCameraDisplayState
    {
        public readonly CameraDisplayConfiguration Configuration;
        bool DoorsWereOpen;
        bool WasAtOrAboveActivationSpeed;
        bool AutomaticallyActive;
        string PreviousPage = "default";

        public CabCameraDisplayState(CameraDisplayConfiguration configuration) => Configuration = configuration;

        public void NotifyManualSelection() => AutomaticallyActive = false;

        public string Update(string currentPage, bool doorsOpen, float speedMpS)
        {
            bool opening = doorsOpen && !DoorsWereOpen;
            DoorsWereOpen = doorsOpen;
            float speed = Math.Abs(speedMpS);
            bool belowActivationSpeed = Configuration.ActivateBelowSpeedMpS > 0 && speed < Configuration.ActivateBelowSpeedMpS;
            // Require an observed crossing from above; starting below the threshold is not a trigger.
            bool enteringLowSpeed = belowActivationSpeed && WasAtOrAboveActivationSpeed;
            WasAtOrAboveActivationSpeed = Configuration.ActivateBelowSpeedMpS > 0 && speed >= Configuration.ActivateBelowSpeedMpS;
            if (!Configuration.ActivateOnDoors && Configuration.ActivateBelowSpeedMpS <= 0) return currentPage;

            // A driver's page selection takes precedence over a pending automatic return.
            if (AutomaticallyActive && currentPage != Configuration.CameraPage) AutomaticallyActive = false;
            if (!AutomaticallyActive && currentPage != Configuration.CameraPage) PreviousPage = currentPage;
            // A driver's camera selection overrides the automatic cutoff.
            if (AutomaticallyActive && speed >= Configuration.HideAboveSpeedMpS)
            {
                AutomaticallyActive = false;
                return Configuration.ReturnPage.Length > 0 ? Configuration.ReturnPage : PreviousPage;
            }
            bool activate = (Configuration.ActivateOnDoors && opening) || enteringLowSpeed;
            if (activate && !AutomaticallyActive && currentPage != Configuration.CameraPage && speed < Configuration.HideAboveSpeedMpS)
            {
                if (currentPage != Configuration.CameraPage) PreviousPage = currentPage;
                AutomaticallyActive = true;
                return Configuration.CameraPage;
            }
            return currentPage;
        }
    }
}
