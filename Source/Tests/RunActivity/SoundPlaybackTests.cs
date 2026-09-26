// COPYRIGHT 2026 by the Open Rails project.
// Licensed under the GNU General Public License, version 3 or later.

using Orts.Viewer3D;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Xunit;

namespace Tests.RunActivity
{
    // Render into memory instead of opening an audio device. No wall-clock waits or audible output.
    public sealed class SoundPlaybackTests : IDisposable
    {
        const int SampleRate = 22050;
        readonly IntPtr Device;
        readonly IntPtr Context;
        readonly string Folder = Path.Combine(Path.GetTempPath(), "orts-sound-test-" + Guid.NewGuid());
        readonly List<SoundStream> Streams = new List<SoundStream>();
        readonly FieldInfo SimulatorField = typeof(ALSoundSource).Assembly.GetType("Orts.Program").GetField("Simulator");
        readonly object PreviousSimulator;
        readonly object Simulator;
        readonly FieldInfo GameTime;
        readonly FieldInfo ClockTime;

        static SoundPlaybackTests()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Native", Environment.Is64BitProcess ? "X64" : "X86", "OpenAL32.dll");
            IntPtr library = NativeLibrary.Load(path);
            NativeLibrary.SetDllImportResolver(typeof(OpenAL).Assembly, (name, assembly, searchPath) => name == "OpenAL32.dll" ? library : IntPtr.Zero);
            NativeLibrary.SetDllImportResolver(typeof(SoundPlaybackTests).Assembly, (name, assembly, searchPath) => name == "OpenAL32.dll" ? library : IntPtr.Zero);
        }

        public SoundPlaybackTests()
        {
            Device = alcLoopbackOpenDeviceSOFT(IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, Device);
            // ALC_FORMAT_CHANNELS_SOFT = stereo; ALC_FORMAT_TYPE_SOFT = signed short.
            Context = OpenAL.alcCreateContext(Device, new[] { 0x1007, SampleRate, 0x1990, 0x1501, 0x1991, 0x1402, 0 });
            Assert.NotEqual(IntPtr.Zero, Context);
            Assert.Equal(1, OpenAL.alcMakeContextCurrent(Context));
            PreviousSimulator = SimulatorField.GetValue(null);
            Simulator = FormatterServices.GetUninitializedObject(SimulatorField.FieldType);
            SimulatorField.SetValue(null, Simulator);
            GameTime = SimulatorField.FieldType.GetField("GameTime");
            ClockTime = SimulatorField.FieldType.GetField("ClockTime");
            Directory.CreateDirectory(Folder);
        }

        [Theory]
        [InlineData(PlayMode.OneShot)]
        [InlineData(PlayMode.Loop)]
        [InlineData(PlayMode.LoopRelease)]
        public void ViewChangeKeepsSourceAndPlaybackOffset(PlayMode mode)
        {
            var stream = CreateStream(true);
            Queue(stream, WriteWave("sound", 4, mode == PlayMode.LoopRelease), mode);
            Advance(0.5);
            int source = stream.ALSoundSource.SoundSourceID;
            int buffer = Get(source, OpenAL.AL_BUFFER);
            int offset = Get(source, OpenAL.AL_BYTE_OFFSET);

            stream.Deactivate();
            Assert.Equal(source, stream.ALSoundSource.SoundSourceID);
            Assert.Equal(offset, Get(source, OpenAL.AL_BYTE_OFFSET));
            OpenAL.alGetSourcef(source, OpenAL.AL_GAIN, out float gain);
            Assert.Equal(0, gain);
            Advance(0.5);
            Assert.Equal(buffer, Get(source, OpenAL.AL_BUFFER));
            Assert.True(Get(source, OpenAL.AL_BYTE_OFFSET) > offset);

            stream.ALSoundSource.Active = true;
            Assert.Equal(source, stream.ALSoundSource.SoundSourceID);
            Assert.Equal(buffer, Get(source, OpenAL.AL_BUFFER));
            Assert.True(Get(source, OpenAL.AL_BYTE_OFFSET) > offset);
        }

        [Fact]
        public void SoundTriggeredInInactiveViewKeepsItsIntroduction()
        {
            var cab = CreateStream(true);
            var external = CreateStream(false);
            Queue(cab, WriteWave("cab", 4, true), PlayMode.LoopRelease);
            Queue(external, WriteWave("external", 4, true), PlayMode.LoopRelease);
            Advance(0.5);
            int source = external.ALSoundSource.SoundSourceID;
            int buffer = Get(source, OpenAL.AL_BUFFER);
            cab.Deactivate();
            external.ALSoundSource.Active = true;
            Advance(0.5);
            Assert.Equal(buffer, Get(source, OpenAL.AL_BUFFER));
            Assert.InRange(Get(source, OpenAL.AL_BYTE_OFFSET), SampleRate, SampleRate * 3);
        }

        [Theory]
        [InlineData(8000)]
        [InlineData(50000)]
        [InlineData(88200)]
        public void MutedOneShotFinishesWithoutReplaying(int bytes)
        {
            var stream = CreateStream(false);
            Queue(stream, WriteWave("oneshot", (double)bytes / (SampleRate * 2)), PlayMode.OneShot);
            Advance(5);
            Assert.Equal(-1, stream.ALSoundSource.SoundSourceID);
            stream.ALSoundSource.Active = true;
            stream.ALSoundSource.Update();
            Assert.Equal(-1, stream.ALSoundSource.SoundSourceID);
            Advance(120);
            Assert.Equal(-1, stream.ALSoundSource.SoundSourceID);
            Assert.False(stream.ALSoundSource.isPlaying);
        }

        [Theory]
        [InlineData(PlayMode.Release)]
        [InlineData(PlayMode.ReleaseWithJump)]
        public void MutedLoopCanFinishItsRelease(PlayMode release)
        {
            var stream = CreateStream(true);
            Queue(stream, WriteWave("loop", 4, true), PlayMode.LoopRelease);
            Advance(2.5);
            stream.Deactivate();
            Queue(stream, "", release);
            Advance(5);
            stream.ALSoundSource.Active = true;
            Advance(1);
            Assert.Equal(-1, stream.ALSoundSource.SoundSourceID);
        }

        [Fact]
        public void SharedWaveHasIndependentLoopCursors()
        {
            var first = CreateStream(true);
            var second = CreateStream(false);
            string file = WriteWave("shared", 4, true);
            Queue(first, file, PlayMode.LoopRelease);
            Advance(2.1); // First stream is in the first sustain segment, with another still to queue.
            int firstLoopBuffer = Get(first.ALSoundSource.SoundSourceID, OpenAL.AL_BUFFER);
            Queue(second, file, PlayMode.LoopRelease);
            Advance(0.65);
            int source = first.ALSoundSource.SoundSourceID;
            int buffer = Get(source, OpenAL.AL_BUFFER);
            var piece = SoundItem.AllPieces[SoundItem.GetKey(file, false, true)];
            Assert.False(piece.isFirst(buffer));
            Assert.NotEqual(firstLoopBuffer, buffer);
            Assert.True(piece.isFirst(Get(second.ALSoundSource.SoundSourceID, OpenAL.AL_BUFFER)));
        }

        [Theory]
        [InlineData(PlayMode.OneShot, false)]
        [InlineData(PlayMode.Loop, true)]
        [InlineData(PlayMode.LoopRelease, true)]
        public void DistanceUnloadDiscardsOneShotsButRetainsLoops(PlayMode mode, bool retained)
        {
            var stream = CreateStream(true);
            Queue(stream, WriteWave("distant", 4, mode == PlayMode.LoopRelease), mode);
            Advance(0.5);
            stream.ALSoundSource.HardDeactivate();
            Assert.Equal(-1, stream.ALSoundSource.SoundSourceID);
            stream.ALSoundSource.HardActivate(true, null);
            Advance(0.1);
            Assert.Equal(retained, stream.ALSoundSource.SoundSourceID != -1);
        }

        SoundStream CreateStream(bool active)
        {
            var owner = (SoundSource)FormatterServices.GetUninitializedObject(typeof(SoundSource));
            var stream = new SoundStream(owner, _ => Array.Empty<ORTSTrigger>());
            stream.ALSoundSource.HardActivate(true, null);
            stream.ALSoundSource.Active = active;
            Streams.Add(stream);
            return stream;
        }

        static void Queue(SoundStream stream, string file, PlayMode mode)
        {
            stream.ALSoundSource.Queue(file, mode, false, true);
            stream.ALSoundSource.Update();
        }

        void Advance(double seconds)
        {
            // Match the frequent sound-update cadence while advancing the offline renderer.
            const int frames = SampleRate / 20;
            var samples = new short[frames * 2];
            for (int step = 0; step < Math.Ceiling(seconds * SampleRate / frames); step++)
            {
                alcRenderSamplesSOFT(Device, samples, frames);
                double time = (double)GameTime.GetValue(Simulator) + (double)frames / SampleRate;
                GameTime.SetValue(Simulator, time);
                ClockTime.SetValue(Simulator, time + 36000);
                foreach (var stream in Streams)
                {
                    // Discrete SMS triggers reapply deactivation on every inactive update.
                    if (!stream.ALSoundSource.Active)
                        stream.Deactivate();
                    stream.ALSoundSource.Update();
                }
            }
        }

        static int Get(int source, int attribute)
        {
            OpenAL.alGetSourcei(source, attribute, out int value);
            return value;
        }

        string WriteWave(string name, double seconds, bool loop = false)
        {
            string path = Path.Combine(Folder, name + ".wav");
            using var writer = new BinaryWriter(File.Create(path));
            int frames = (int)Math.Round(seconds * SampleRate);
            writer.Write("RIFF".ToCharArray());
            int[] cues = loop ? new[] { SampleRate * 2, SampleRate * 5 / 2, SampleRate * 3 } : Array.Empty<int>();
            writer.Write(36 + frames * 2 + (loop ? 12 + cues.Length * 24 : 0));
            writer.Write("WAVEfmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(frames * 2);
            writer.Write(new byte[frames * 2]);
            if (loop)
            {
                writer.Write("cue ".ToCharArray());
                writer.Write(4 + cues.Length * 24);
                writer.Write(cues.Length);
                foreach (int cue in cues)
                {
                    writer.Write(cue);
                    writer.Write(cue);
                    writer.Write("data".ToCharArray());
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(cue);
                }
            }
            return path;
        }

        public void Dispose()
        {
            foreach (var stream in Streams)
            {
                stream.ALSoundSource.HardDeactivate();
                stream.ALSoundSource.Dispose();
            }
            foreach (var piece in SoundItem.AllPieces.Values)
                piece.Dispose();
            SoundItem.AllPieces.Clear();
            SimulatorField.SetValue(null, PreviousSimulator);
            OpenAL.alcMakeContextCurrent(IntPtr.Zero);
            alcDestroyContext(Context);
            alcCloseDevice(Device);
            // The legacy WAV loader leaves its file stream for finalization.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Directory.Delete(Folder, true);
        }

        [DllImport("OpenAL32.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr alcLoopbackOpenDeviceSOFT(IntPtr name);
        [DllImport("OpenAL32.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void alcRenderSamplesSOFT(IntPtr device, [Out] short[] buffer, int samples);
        [DllImport("OpenAL32.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void alcDestroyContext(IntPtr context);
        [DllImport("OpenAL32.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern bool alcCloseDevice(IntPtr device);
    }
}
