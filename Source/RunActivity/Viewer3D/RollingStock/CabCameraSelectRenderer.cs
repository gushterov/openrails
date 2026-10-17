// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orts.Formats.Msts;
using Orts.Simulation.RollingStocks;
using Orts.Viewer3D.Popups;
using ORTS.Common;
using ORTS.Common.Input;

namespace Orts.Viewer3D.RollingStock
{
    public sealed class CabCameraSelectRenderer : CabViewControlRenderer, ICabViewMouseControlRenderer
    {
        readonly CVCCameraSelect Configuration;
        Rectangle Rectangle;
        WindowTextFont Font;
        public CabCameraSelectRenderer(Viewer viewer, MSTSLocomotive locomotive, CVCCameraSelect control)
            : base(viewer, locomotive, control, null) => Configuration = control;

        public override void PrepareFrame(RenderFrame frame, ElapsedTime elapsedTime)
        {
            if (frame.IsCameraFeed || !Control.IsVisible || (!IsPowered && Control.HideIfDisabled)) return;
            float xScale = Viewer.CabWidthPixels / 640f;
            float yScale = Viewer.CabHeightPixels / 480f;
            Rectangle = new Rectangle((int)(Control.PositionX * xScale) - Viewer.CabXOffsetPixels + Viewer.CabXLetterboxPixels,
                (int)(Control.PositionY * yScale) + Viewer.CabYOffsetPixels + Viewer.CabYLetterboxPixels,
                (int)(Control.Width * xScale), (int)(Control.Height * yScale));
            if (Configuration.DrawLabel)
            {
                Font = Viewer.WindowManager.TextManager.GetExact("Arial", Viewer.CabHeightPixels * 3.5f / 480, System.Drawing.FontStyle.Regular);
                var matrix = Matrix.Identity;
                frame.AddPrimitive(ControlView, this, RenderPrimitiveGroup.Cab, ref matrix);
            }
        }

        public override void Draw(GraphicsDevice graphicsDevice) => Font?.Draw(ControlView.SpriteBatch,
            Rectangle, Point.Zero, 0,
            Control.Label, LabelAlignment.Center, Color.White, Color.Black);

        public bool IsMouseWithin() => Rectangle.Contains(UserInput.MouseX, UserInput.MouseY);
        public void HandleUserInput()
        {
            if (UserInput.IsMouseLeftButtonPressed)
                new SelectScreenCommand(Viewer.Log, true, Configuration.TargetPage, Control.Display);
        }
        public string GetControlName() => Control.Label;
        public string ControlLabel => Control.Label;
        public Rectangle DestinationRectangleGet() => Rectangle;
        public bool isMouseControl() => true;
    }
}
