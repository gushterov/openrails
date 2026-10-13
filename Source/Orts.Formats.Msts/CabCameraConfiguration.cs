// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Microsoft.Xna.Framework;
using Orts.Parsers.Msts;

namespace Orts.Formats.Msts
{
    /// <summary>A live camera feed, independently positioned on a cab display.</summary>
    public class CVCCamera : CabViewControl
    {
        public Vector3 CameraPosition;
        // Degrees: yaw to the right, pitch up, roll clockwise; zero looks along local +Z.
        public Vector3 CameraDirection;
        public Vector3? RearCameraPosition;
        public Vector3? RearCameraDirection;
        public int CarOffset;
        public float FieldOfView = 60;
        public int ImageWidth = 320;
        public int ImageHeight = 240;
        public float FramesPerSecond = 10;
        public bool Mirror;
        public float Rotation;
        // Optional corners in cab coordinates: top-left, top-right, bottom-right, bottom-left.
        public Vector2[] ScreenCorners;

        public CVCCamera(STFReader stf, string basepath)
        {
            ControlType = new CabViewControlType(CABViewControlTypes.ORTS_CAMERA);
            stf.MustMatch("(");
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("position", () => ParsePosition(stf)),
                new STFReader.TokenProcessor("graphic", () => ParseGraphic(stf, basepath)),
                new STFReader.TokenProcessor("ortsdisplay", () => ParseDisplay(stf)),
                new STFReader.TokenProcessor("ortsscreenpage", () => ParseScreen(stf)),
                new STFReader.TokenProcessor("ortscabviewpoint", () => ParseCabViewpoint(stf)),
                new STFReader.TokenProcessor("cameraposition", () => CameraPosition = ReadVector(stf)),
                new STFReader.TokenProcessor("cameradirection", () => CameraDirection = ReadVector(stf)),
                new STFReader.TokenProcessor("rearcameraposition", () => RearCameraPosition = ReadVector(stf)),
                new STFReader.TokenProcessor("rearcameradirection", () => RearCameraDirection = ReadVector(stf)),
                new STFReader.TokenProcessor("caroffset", () => CarOffset = stf.ReadIntBlock(0)),
                new STFReader.TokenProcessor("fieldofview", () => FieldOfView = stf.ReadFloatBlock(STFReader.UNITS.None, 60)),
                new STFReader.TokenProcessor("imagesize", () => {
                    stf.MustMatch("(");
                    ImageWidth = stf.ReadInt(320);
                    ImageHeight = stf.ReadInt(240);
                    stf.SkipRestOfBlock();
                }),
                new STFReader.TokenProcessor("updaterate", () => FramesPerSecond = stf.ReadFloatBlock(STFReader.UNITS.None, 10)),
                new STFReader.TokenProcessor("mirror", () => Mirror = stf.ReadBoolBlock(false)),
                new STFReader.TokenProcessor("ortsangle", () => Rotation = ParseRotation(stf)),
                new STFReader.TokenProcessor("screencorners", () => {
                    stf.MustMatch("(");
                    ScreenCorners = new Vector2[4];
                    for (int i = 0; i < 4; i++) ScreenCorners[i] = new Vector2(
                        stf.ReadFloat(STFReader.UNITS.None, 0), stf.ReadFloat(STFReader.UNITS.None, 0));
                    stf.SkipRestOfBlock();
                }),
                new STFReader.TokenProcessor("disablediflowvoltagepowersupplyoff", () => ParseDisabledIfLowVoltagePowerSupplyOff(stf)),
                new STFReader.TokenProcessor("disabledifcabpowersupplyoff", () => ParseDisabledIfCabPowerSupplyOff(stf)),
                new STFReader.TokenProcessor("hideifdisabled", () => ParseHideIfDisabled(stf)),
            });
            FieldOfView = FiniteClamp(FieldOfView, 60, 5, 120);
            FramesPerSecond = FiniteClamp(FramesPerSecond, 10, 1, 30);
            ImageWidth = Math.Clamp(ImageWidth, 16, 1024);
            ImageHeight = Math.Clamp(ImageHeight, 16, 1024);
            if (!IsFinite(CameraPosition)) CameraPosition = Vector3.Zero;
            if (!IsFinite(CameraDirection)) CameraDirection = Vector3.Zero;
            if (RearCameraPosition.HasValue && !IsFinite(RearCameraPosition.Value)) RearCameraPosition = null;
            if (RearCameraDirection.HasValue && !IsFinite(RearCameraDirection.Value)) RearCameraDirection = null;
            if (!float.IsFinite(Rotation)) Rotation = 0;
            if (ScreenCorners != null && Array.Exists(ScreenCorners, point => !float.IsFinite(point.X) || !float.IsFinite(point.Y))) ScreenCorners = null;
            if (!double.IsFinite(PositionX) || !double.IsFinite(PositionY)) PositionX = PositionY = 0;
            if (!double.IsFinite(Width) || Width <= 0 || Width > 2048) Width = 160;
            if (!double.IsFinite(Height) || Height <= 0 || Height > 2048) Height = 120;
            Display = Math.Clamp(Display, 0, 7);
        }

        static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        static float FiniteClamp(float value, float fallback, float min, float max) =>
            float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

        static Vector3 ReadVector(STFReader stf)
        {
            stf.MustMatch("(");
            var value = new Vector3(stf.ReadFloat(STFReader.UNITS.None, 0),
                stf.ReadFloat(STFReader.UNITS.None, 0), stf.ReadFloat(STFReader.UNITS.None, 0));
            stf.SkipRestOfBlock();
            return value;
        }
    }

    /// <summary>A page-selection hotspot over an existing button or touchscreen.</summary>
    public class CVCCameraSelect : CabViewControl
    {
        public string TargetPage = "default";
        public bool DrawLabel;
        public CVCCameraSelect(STFReader stf)
        {
            ControlType = new CabViewControlType(CABViewControlTypes.ORTS_SCREEN_SELECT);
            stf.MustMatch("(");
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("position", () => ParsePosition(stf)),
                new STFReader.TokenProcessor("ortsdisplay", () => ParseDisplay(stf)),
                new STFReader.TokenProcessor("ortsscreenpage", () => ParseScreen(stf)),
                new STFReader.TokenProcessor("ortscabviewpoint", () => ParseCabViewpoint(stf)),
                new STFReader.TokenProcessor("targetpage", () => TargetPage = stf.ReadStringBlock("default").ToLowerInvariant()),
                new STFReader.TokenProcessor("label", () => Label = stf.ReadStringBlock("")),
                new STFReader.TokenProcessor("drawlabel", () => DrawLabel = stf.ReadBoolBlock(false)),
            });
            Display = Math.Clamp(Display, 0, 7);
        }
    }

    /// <summary>One switching policy for all camera items on a display.</summary>
    public class CameraDisplayConfiguration
    {
        public int Display;
        public string CameraPage = "cameras";
        public bool ActivateOnDoors;
        // Zero disables automatic activation when entering the low-speed range.
        public float ActivateBelowSpeedMpS;
        public float HideAboveSpeedMpS = 5 / 3.6f;
        // Empty means restore the page selected before automatic activation.
        public string ReturnPage = "";

        public CameraDisplayConfiguration() { }

        public CameraDisplayConfiguration(STFReader stf)
        {
            stf.MustMatch("(");
            stf.ParseBlock(new[] {
                new STFReader.TokenProcessor("ortsdisplay", () => Display = stf.ReadIntBlock(0)),
                new STFReader.TokenProcessor("camerapage", () => CameraPage = stf.ReadStringBlock("cameras").ToLowerInvariant()),
                new STFReader.TokenProcessor("activateondoors", () => ActivateOnDoors = stf.ReadBoolBlock(false)),
                new STFReader.TokenProcessor("activatebelowspeed", () => ActivateBelowSpeedMpS = stf.ReadFloatBlock(STFReader.UNITS.Speed, 0)),
                new STFReader.TokenProcessor("hideabovespeed", () => HideAboveSpeedMpS = stf.ReadFloatBlock(STFReader.UNITS.Speed, 5 / 3.6f)),
                new STFReader.TokenProcessor("returnpage", () => ReturnPage = stf.ReadStringBlock("").ToLowerInvariant()),
            });
            if (!float.IsFinite(HideAboveSpeedMpS) || HideAboveSpeedMpS <= 0) HideAboveSpeedMpS = 5 / 3.6f;
            if (!float.IsFinite(ActivateBelowSpeedMpS) || ActivateBelowSpeedMpS < 0) ActivateBelowSpeedMpS = 0;
        }
    }
}
