// See https://aka.ms/new-console-template for more information

using System.Reflection;

namespace nadena.dev.resonity.remote.bootstrap;

internal class Program
{
    [STAThread]
    private static async Task<int> Main(
        string[] args
    )
    {
        // Add the application directory to the assembly load path early, so Launcher can use System.CommandLine
        var appDir = AppContext.BaseDirectory;
        Console.WriteLine("Base directory: " + appDir);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string assemblyPath = Path.Combine(appDir, new AssemblyName(args.Name).Name + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }
            else
            {
                return null;
            }
        };

        return await new Launcher().Launch(args);
    }
}