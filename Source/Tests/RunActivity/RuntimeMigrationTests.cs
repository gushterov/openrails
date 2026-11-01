// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using Orts.Common.Scripting;
using Xunit;

namespace Tests.RunActivity
{
    public class RuntimeMigrationTests
    {
        [Fact]
        public void ScriptsCompileAgainstRuntimeCommonAndSimulationAssemblies()
        {
            using var file = new TestFile(@"
                using System.Collections.Generic;
                using System.Linq;
                using ORTS.Scripting.Api;
                public class RuntimeProbe : AbstractScriptClass
                {
                    public int Value() => new List<int> { 1, 2, 3 }.Sum();
                    public System.Type ApiType() => typeof(Timer);
                    public System.Type CommonType() => typeof(ORTS.Common.ElapsedTime);
                }");
            var assembly = (Assembly)typeof(ScriptManager).GetMethod("CompileScript", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { new[] { file.FileName } });
            Assert.NotNull(assembly);
            var script = Activator.CreateInstance(assembly.GetType("RuntimeProbe"));
            Assert.Equal(6, script.GetType().GetMethod("Value").Invoke(script, null));
            Assert.Equal(typeof(ORTS.Scripting.Api.Timer), script.GetType().GetMethod("ApiType").Invoke(script, null));
            Assert.Equal(typeof(ORTS.Common.ElapsedTime), script.GetType().GetMethod("CommonType").Invoke(script, null));
        }

        [Theory]
        [InlineData("Menu")]
        [InlineData("RunActivity")]
        public void EmbeddedResourcesLoadOnTheCurrentRuntime(string assemblyName)
        {
            var assembly = Assembly.Load(assemblyName);
            int count = 0;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.EndsWith(".resources", StringComparison.Ordinal)) continue;
                var manager = new ResourceManager(name.Substring(0, name.Length - ".resources".Length), assembly);
                try
                {
                    var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
                    Assert.NotNull(resources);
                    foreach (DictionaryEntry entry in resources)
                    {
                        // Reading the value exercises icons, bitmaps, fonts, and form metadata.
                        _ = entry.Value;
                        count++;
                    }
                }
                finally { manager.ReleaseAllResources(); }
            }
            Assert.True(count > 0);
        }
    }
}
