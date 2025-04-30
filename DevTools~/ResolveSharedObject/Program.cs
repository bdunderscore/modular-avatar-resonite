using System.Diagnostics;
using System.IO.Compression;

Console.WriteLine("Get .so ===");
using var httpClient = new HttpClient();


async Task GetLibFromDebianPackage(string url, string libFileName)
{
    Console.WriteLine("Get package name: " + libFileName);
    Console.WriteLine("Get package url: " + url);

    var currentDirectory = Directory.GetCurrentDirectory();
    Console.WriteLine("current dir: " + currentDirectory);
    var tempDir = Directory.CreateTempSubdirectory().ToString();
    Console.WriteLine("temp dir: " + tempDir);

    var debPackage = await httpClient.GetAsync(url);

    Directory.SetCurrentDirectory(tempDir);
    var debFileName = "package.deb";
    using (var file = File.Open(debFileName, FileMode.Create))
        await debPackage.Content.CopyToAsync(file);

    var unpacking = Process.Start("ar", ["x", debFileName]);
    if (unpacking is null) { throw new Exception(); }
    await unpacking.WaitForExitAsync();
    Console.WriteLine("ar exit code:" + unpacking.ExitCode);

    var unpackingXZ = Process.Start("xz", ["-d", "data.tar.xz"]);
    if (unpackingXZ is null) { throw new Exception(); }
    await unpackingXZ.WaitForExitAsync();
    Console.WriteLine("xz exit code:" + unpackingXZ.ExitCode);

    var unpackingTar = Process.Start("tar", ["-x", "-f", "data.tar"]);
    if (unpackingTar is null) { throw new Exception(); }
    await unpackingTar.WaitForExitAsync();
    Console.WriteLine("tar exit code:" + unpackingTar.ExitCode);

    Console.WriteLine("===");
    var binPath = Path.Combine("usr", "lib", "x86_64-linux-gnu");
    foreach (var file in Directory.GetFiles(binPath))
    {
        Console.WriteLine(file);
    }
    Console.WriteLine("===");

    var libFile = await File.ReadAllBytesAsync(Path.Combine(binPath, libFileName));

    Directory.SetCurrentDirectory(currentDirectory);
    Directory.Delete(tempDir, true);

    var outPath = Path.Combine("ResoPuppet~", libFileName);
    File.WriteAllBytes(outPath, libFile);
    Console.WriteLine("lib out put: " + outPath);

    Console.WriteLine("===");
}

async Task GetLibFromNuget(string url, string libEntry)
{
    Console.WriteLine("Get package name: " + libEntry);
    Console.WriteLine("Get package url: " + url);

    var nugetPackage = await httpClient.GetAsync(url);
    using var unGetZip = new ZipArchive(nugetPackage.Content.ReadAsStream(), ZipArchiveMode.Read);

    var entry = unGetZip.GetEntry(libEntry);
    if (entry is null) { throw new Exception(); }

    var outPath = Path.Combine("ResoPuppet~", Path.GetFileName(libEntry));
    entry.ExtractToFile(outPath, true);
    Console.WriteLine("lib out put: " + outPath);

    Console.WriteLine("===");
}


await GetLibFromDebianPackage("http://ftp.jp.debian.org/debian/pool/main/a/assimp/libassimp5_5.4.3+ds-2_amd64.deb", "libassimp.so.5");

await GetLibFromNuget("https://www.nuget.org/api/v2/package/FreeImage-dotnet-core/4.3.6", "runtimes/ubuntu.16.04-x64/native/FreeImage.so");
await GetLibFromNuget("https://www.nuget.org/api/v2/package/OpenRA-Freetype6/1.0.11", "native/linux-x64/freetype6.so");
