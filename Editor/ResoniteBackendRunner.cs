#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using nadena.dev.ndmf.proto;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;
using OSPlatform = System.Runtime.InteropServices.OSPlatform;

namespace nadena.dev.ndmf.platform.resonite
{
    /// <summary>
    /// Runs the ResoniteHook backend as a one-shot batch process: the serialized avatar is
    /// written to a temp input file, the backend is invoked to convert it, and the resulting
    /// .resonitepackage is read from a temp output file. Logs and progress are streamed over
    /// the child process' stdout/stderr.
    /// </summary>
    internal static class ResoniteBackendRunner
    {
        // Must match PuppeteerCommon/BatchProtocol.cs on the backend side.
        private const string ControlPrefix = "MA-RESO ";
        private const string ProgressPrefix = ControlPrefix + "PROGRESS ";
        private const string ErrorPrefix = ControlPrefix + "ERROR ";
        private const string StructuredErrorPrefix = ControlPrefix + "STRUCTURED_ERROR ";
        private const string DonePrefix = ControlPrefix + "DONE";

        private static readonly string ExecutableBinaryExtension =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

        private static string ResoPuppetDir
        {
            get
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(ResoniteBackendRunner).Assembly);
                if (packageInfo == null)
                {
                    throw new Exception("Could not resolve the nadena.dev.modular-avatar.resonite package location");
                }

                return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "ResoPuppet~"));
            }
        }

        public static async Task<bool> RunBuild(
            ExportRoot root,
            string outputPath,
            Action<string> onProgress,
            CancellationToken ct)
        {
            var libraryPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Library");
            var tempDir = Path.Combine(libraryPath, "ResonitePuppet");
            Directory.CreateDirectory(tempDir);

            var inputPath = Path.Combine(tempDir, "reso-input.pb");
            var bytes = root.ToByteArray();
            File.WriteAllBytes(inputPath, bytes);

#if NDMF_DEBUG
            WriteDebugDump(bytes, outputPath);
#endif

            var cwd = ResoPuppetDir;
            var exe = Path.Combine(cwd, "Launcher" + ExecutableBinaryExtension);

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                ArgumentList =
                {
                    "--input", inputPath,
                    "--output", outputPath,
                    "--temp-directory", tempDir,
                },
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            var processExited = new TaskCompletionSource<int>();

            process.OutputDataReceived += (_, e) => HandleOutputLine(e.Data, onProgress);
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) Debug.LogError("[MA-Resonite] " + e.Data);
            };
            process.Exited += (_, _) => processExited.TrySetResult(process.ExitCode);

            void KillChild(object? sender, EventArgs e)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to kill Resonite backend: {ex}");
                }
            }

            if (!process.Start())
            {
                throw new Exception("Failed to start Resonite Launcher");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            AppDomain.CurrentDomain.DomainUnload += KillChild;
            AppDomain.CurrentDomain.ProcessExit += KillChild;

            try
            {
                using (ct.Register(() =>
                       {
                           try { if (!process.HasExited) process.Kill(); }
                           catch (Exception ex) { Debug.LogException(ex); }
                       }))
                {
                    var exitCode = await processExited.Task;
                    var success = exitCode == 0 && File.Exists(outputPath);

                    if (success)
                    {
                        try { File.Delete(inputPath); }
                        catch (Exception ex) { Debug.LogException(ex); }
                    }

                    return success;
                }
            }
            finally
            {
                AppDomain.CurrentDomain.DomainUnload -= KillChild;
                AppDomain.CurrentDomain.ProcessExit -= KillChild;
            }
        }

        private static void HandleOutputLine(string? line, Action<string> onProgress)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (line.StartsWith(ProgressPrefix))
            {
                var message = line.Substring(ProgressPrefix.Length);
                onProgress(message);
                Debug.Log("[MA-Resonite progress] " + message);
            }
            else if (line.StartsWith(ErrorPrefix))
            {
                Debug.LogError("[MA-Resonite] " + line.Substring(ErrorPrefix.Length));
            }
            else if (line.StartsWith(StructuredErrorPrefix))
            {
                // TODO: NDMF structured error reporting
            }
            else if (line.StartsWith(DonePrefix))
            {
                // no-op; success is determined from the process exit code
            }
            else
            {
                Debug.Log("[MA-Resonite] " + line);
            }
        }

#if NDMF_DEBUG
        private static void WriteDebugDump(byte[] bytes, string outputPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var debugDir = Path.Combine(projectRoot, "ResoniteDebug");
            Directory.CreateDirectory(debugDir);

            var dumpPath = Path.Combine(debugDir, "last-export.pb");
            File.WriteAllBytes(dumpPath, bytes);

            var exe = Path.Combine(ResoPuppetDir, "Launcher" + ExecutableBinaryExtension);
            Debug.Log("[MA-Resonite] Wrote debug export dump to " + dumpPath + "\n" +
                      $"Invoke with: \"{exe}\" --input \"{dumpPath}\" --output \"{outputPath}\" --temp-directory \"<temp-dir>\"");
        }
#endif
    }
}
