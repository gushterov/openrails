// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Orts.Common;
using Orts.Viewer3D;
using Xunit;

namespace Tests.Orts.Simulation
{
    public class ReplayLogSerializerTests : IDisposable
    {
        readonly TraceListener[] Listeners;

        public ReplayLogSerializerTests()
        {
            Listeners = new TraceListener[Trace.Listeners.Count];
            Trace.Listeners.CopyTo(Listeners, 0);
            Trace.Listeners.Clear();
        }

        public void Dispose()
        {
            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(Listeners);
        }

        static MemoryStream LegacyFixture(string name)
        {
            using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "ReplayLogs", name + ".replay.gz"));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var stream = new MemoryStream();
            gzip.CopyTo(stream);
            stream.Position = 0;
            return stream;
        }

        static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var parent = type; parent != typeof(object); parent = parent.BaseType)
                foreach (var field in parent.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsDefined(typeof(NonSerializedAttribute), false)) yield return field;
        }

        [Fact]
        public void EveryNet6CommandLoadsWithPrivateAndInheritedStateIntact()
        {
            using var stream = LegacyFixture("net6-all-commands");
            var commands = ReplayLogSerializer.Read(stream);
            Assert.Equal(155, commands.Count);
            Assert.Contains(commands, command => command is CameraRotateUpDownCommand);
            foreach (var command in commands)
            {
                Assert.Equal(7, command.Time);
                foreach (var field in Fields(command.GetType()))
                {
                    var type = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                    object expected = type == typeof(string) ? "legacy-test" : type == typeof(bool) ? true
                        : type.IsEnum ? Enum.GetValues(type).GetValue(0) : Convert.ChangeType(7, type);
                    Assert.Equal(expected, field.GetValue(command));
                }
            }
        }

        [Fact]
        public void AllCurrentCommandsRoundTripWithoutRunningTheirConstructors()
        {
            var types = new[] { typeof(Command).Assembly, typeof(CameraRotateUpDownCommand).Assembly }
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => !type.IsAbstract && typeof(Command).IsAssignableFrom(type)).ToArray();
            var commands = types.Select(type => (ICommand)RuntimeHelpers.GetUninitializedObject(type)).ToList();
            using var stream = new MemoryStream();
            ReplayLogSerializer.Write(stream, commands);
            stream.Position = 0;
            var loaded = ReplayLogSerializer.Read(stream);
            Assert.Equal(types, loaded.Select(command => command.GetType()));
        }

        [Fact]
        public void LegacyCommandsCanBeResavedInTheNewFormatWithoutLosingState()
        {
            using var legacy = LegacyFixture("net6-all-commands");
            var original = ReplayLogSerializer.Read(legacy);
            using var stream = new MemoryStream();
            ReplayLogSerializer.Write(stream, original);
            Assert.Equal((byte)'{', stream.GetBuffer()[0]);
            stream.Position = 0;
            var loaded = ReplayLogSerializer.Read(stream);
            Assert.Equal(original.Count, loaded.Count);
            for (int index = 0; index < original.Count; index++)
            {
                Assert.Equal(original[index].GetType(), loaded[index].GetType());
                foreach (var field in Fields(original[index].GetType()))
                    Assert.Equal(field.GetValue(original[index]), field.GetValue(loaded[index]));
            }
        }

        [Fact]
        public void EmptyNet6LogRemainsReadable()
        {
            using var stream = LegacyFixture("net6-empty");
            Assert.Empty(ReplayLogSerializer.Read(stream));
        }

        [Fact]
        public void LegacyBrakeFloodStillCompactsWhenLoadedByCommandLog()
        {
            using var stream = LegacyFixture("net6-brake-flood");
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, stream.ToArray());
                var log = new CommandLog(null);
                log.LoadLog(path);
                Assert.Equal(2, log.CommandList.Count);
                Assert.Equal(100, log.CommandList[0].Time);
                Assert.Equal(200, log.CommandList[1].Time);
                Assert.Equal(0.75f, typeof(ContinuousCommand).GetField("Target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(log.CommandList[1]));
                log.SaveLog(path);
                Assert.True(new FileInfo(path).Length < 4096);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("{\"Version\":2,\"Commands\":[]}")]
        [InlineData("{\"Version\":1,\"Commands\":[{\"Type\":\"System.IO.FileInfo\",\"Fields\":{}}]}")]
        [InlineData("{\"Version\":1,\"Commands\":[{\"Type\":\"Orts.Common.BellCommand\",\"Fields\":{\"Unknown\":0}}]}")]
        [InlineData("{\"Version\":1,\"Commands\":[{\"Type\":\"Orts.Common.BellCommand\",\"Fields\":{\"BooleanCommand.ToState\":{}}}]}")]
        [InlineData("{")]
        public void InvalidOrUnknownReplayDataIsRejected(string json)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Assert.Throws<InvalidDataException>(() => ReplayLogSerializer.Read(stream));
        }

        [Fact]
        public void InvalidFileDoesNotReplaceAnExistingCommandLog()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{\"Version\":2,\"Commands\":[]}");
                var command = (ICommand)RuntimeHelpers.GetUninitializedObject(typeof(BellCommand));
                var log = new CommandLog(null);
                log.CommandList.Add(command);
                log.LoadLog(path);
                Assert.Same(command, Assert.Single(log.CommandList));
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void FloatingPointCommandValuesRemainRepresentable(float target)
        {
            var command = (ICommand)RuntimeHelpers.GetUninitializedObject(typeof(TrainBrakeCommand));
            var field = typeof(ContinuousCommand).GetField("Target", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(command, (float?)target);
            using var stream = new MemoryStream();
            ReplayLogSerializer.Write(stream, new[] { command });
            stream.Position = 0;
            Assert.Equal(target, field.GetValue(Assert.Single(ReplayLogSerializer.Read(stream))));
        }
    }
}
