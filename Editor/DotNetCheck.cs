using System.Linq;

namespace nadena.dev.ndmf.platform.resonite
{
    internal static class DotNetCheck
    {
        private static bool CheckPassed = false;
        
        public static bool CheckDotNetVersions()
        {
            if (CheckPassed) return true;
            
            // Invoke dotnet --list-runtimes
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = "--list-runtimes";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            string[] output = process.StandardOutput.ReadToEnd().Split("\n");

            CheckPassed = output.Any(s => s.StartsWith("Microsoft.WindowsDesktop.App 9."));

            return CheckPassed;
        }
    }
}