using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace nadena.dev.ndmf.platform.resonite
{
    internal static class ResolveSharedObject
    {
        internal static async Task DoResolve()
        {
            var outDir = Path.GetFullPath(RPCClientController.RESOPUPPET_DIR);
            var resolvedMarker = Path.Combine(outDir, "SharedObjectResolvedMarker.txt");
            if (File.Exists(resolvedMarker)) { return; }
            await File.WriteAllTextAsync(resolvedMarker, "SharedObjectResolved! generate this text from moduler avatar resonite.");

            using var httpClient = new HttpClient();
            async Task GetLibFromNuget(string url, IEnumerable<string> libEntry)
            {

                var nugetPackage = await httpClient.GetAsync(url);
                using var unGetZip = new ZipArchive(await nugetPackage.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);

                foreach (var entryPath in libEntry)
                {
                    var entry = unGetZip.GetEntry(entryPath);
                    if (entry is null)
                    {
                        Debug.Log("lib entry not found: " + entryPath);
                        continue;
                    }

                    entry.ExtractToFile(Path.Combine(outDir, Path.GetFileName(entryPath)), true);
                }
            }

            Task.WaitAll(NeedSharedObjects().Select(a => GetLibFromNuget(a.url, a.libEntry)).ToArray());
        }

        static IEnumerable<(string url, IEnumerable<string> libEntry)> NeedSharedObjects()
        {
            yield return (
                "https://www.nuget.org/api/v2/package/Ultz.Native.Assimp/5.4.1",
                new[] { "runtimes/linux-x64/native/libassimp.so.5" }
                );

            yield return (
                "https://www.nuget.org/api/v2/package/FreeImage-dotnet-core/4.3.6",
                new[] { "runtimes/ubuntu.16.04-x64/native/FreeImage.so" }
                );

            yield return (
                "https://www.nuget.org/api/v2/package/OpenRA-Freetype6/1.0.11",
                new[] { "native/linux-x64/freetype6.so" }
                );

            yield return (
                "https://www.nuget.org/api/v2/package/SteamAudio.NET.Natives/4.5.3",
                new[] { "runtimes/linux-x64/native/libphonon.so" }
                );
        }
    }
}
