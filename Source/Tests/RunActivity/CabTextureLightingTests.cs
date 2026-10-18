// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Orts.Viewer3D.RollingStock;
using Xunit;

namespace Tests.RunActivity
{
    public class CabTextureLightingTests
    {
        sealed class LightingState
        {
            public bool IsNightTexture = true;
        }

        sealed class PausedLookup : IEqualityComparer<string>, IDisposable
        {
            public readonly ManualResetEventSlim Started = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Continue = new ManualResetEventSlim();
            public bool Armed;

            public bool Equals(string x, string y) => StringComparer.Ordinal.Equals(x, y);

            public int GetHashCode(string value)
            {
                if (Armed)
                {
                    Started.Set();
                    if (!Continue.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Cab texture lookup was not released.");
                }
                return StringComparer.Ordinal.GetHashCode(value);
            }

            public void Dispose()
            {
                Started.Dispose();
                Continue.Dispose();
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void NightLightingRemainsValidWhileNextTextureIsSelected(bool splitTexture, bool cabLight)
        {
            const string path = "cab-lighting-test.ace";
            var texture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            GC.SuppressFinalize(texture); // Only identity is used; no GPU resource was allocated.
            using var lookup = new PausedLookup();
            string prefix = splitTexture ? "P" : "";
            var previous = new Dictionary<FieldInfo, object>();
            foreach (string kind in new[] { "Day", "Night", "Light" })
            {
                var field = typeof(CABTextureManager).GetField(prefix + kind + "Textures", BindingFlags.Static | BindingFlags.NonPublic);
                previous.Add(field, field.GetValue(null));
                var comparer = kind == "Day" ? (IEqualityComparer<string>)lookup : StringComparer.Ordinal;
                object textures = splitTexture
                    ? (object)new Dictionary<string, Texture2D[]>(comparer) { [path] = new[] { texture } }
                    : new Dictionary<string, Texture2D>(comparer) { [path] = texture };
                field.SetValue(null, textures);
            }

            var state = new LightingState();
            Task<Texture2D> selecting = null;
            try
            {
                lookup.Armed = true;
                selecting = Task.Run(() => splitTexture
                    ? CABTextureManager.GetTextureByIndexes(path, 0, true, cabLight, out state.IsNightTexture, true)
                    : CABTextureManager.GetTexture(path, true, cabLight, out state.IsNightTexture, true));
                Assert.True(lookup.Started.Wait(TimeSpan.FromSeconds(10)), "Texture selection did not start.");
                // The rendering thread must still see the previous valid lighting state.
                Assert.True(state.IsNightTexture);
                lookup.Continue.Set();
                Assert.Same(texture, selecting.GetAwaiter().GetResult());
                Assert.True(state.IsNightTexture);
            }
            finally
            {
                lookup.Continue.Set();
                try { selecting?.GetAwaiter().GetResult(); }
                finally
                {
                    foreach (var item in previous) item.Key.SetValue(null, item.Value);
                }
            }
        }
    }
}
