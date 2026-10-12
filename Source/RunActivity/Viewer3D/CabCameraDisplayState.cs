// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Orts.Formats.Msts;

namespace Orts.Viewer3D
{
    /// <summary>Door activation is an edge, so slowing down cannot re-open the page.</summary>
    public sealed class CabCameraDisplayState
    {
        public readonly CameraDisplayConfiguration Configuration;
        bool DoorsWereOpen;
        bool AutomaticallyActive;
        string PreviousPage = "default";

        public CabCameraDisplayState(CameraDisplayConfiguration configuration) => Configuration = configuration;

        public void NotifyManualSelection() => AutomaticallyActive = false;

        public string Update(string currentPage, bool doorsOpen, float speedMpS)
        {
            bool opening = doorsOpen && !DoorsWereOpen;
            DoorsWereOpen = doorsOpen;
            if (!Configuration.ActivateOnDoors) return currentPage;

            // A driver's page selection takes precedence over a pending automatic return.
            if (AutomaticallyActive && currentPage != Configuration.CameraPage) AutomaticallyActive = false;
            if (!AutomaticallyActive && currentPage != Configuration.CameraPage) PreviousPage = currentPage;
            // Hide only the view opened by a door event; a driver's camera selection overrides the cutoff.
            if (AutomaticallyActive && Math.Abs(speedMpS) >= Configuration.HideAboveSpeedMpS)
            {
                AutomaticallyActive = false;
                return Configuration.ReturnPage.Length > 0 ? Configuration.ReturnPage : PreviousPage;
            }
            if (opening && !AutomaticallyActive && currentPage != Configuration.CameraPage && Math.Abs(speedMpS) < Configuration.HideAboveSpeedMpS)
            {
                if (currentPage != Configuration.CameraPage) PreviousPage = currentPage;
                AutomaticallyActive = true;
                return Configuration.CameraPage;
            }
            return currentPage;
        }
    }
}
