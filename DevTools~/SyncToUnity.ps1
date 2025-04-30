dotnet build Resonite~/ResoniteHook/ResoniteHook.sln
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -Path "./ResoPuppet~" -ItemType Directory -Force
Copy-Item -Path "Resonite~/ResoniteHook/ResoPuppetSchema/bin/*" -Destination "./Managed" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/Debug/net9.0/*.dll" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/Debug/net9.0/*.exe" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/Debug/net9.0/*.pdb" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/Debug/net9.0/*.json" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/Debug/net9.0/*.dll" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/Debug/net9.0/*.pdb" -Destination "./ResoPuppet~" -Recurse -Force

Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/Debug/net9.0/Launcher" -Destination "./ResoPuppet~" -Force
Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/Debug/net9.0/Puppeteer" -Destination "./ResoPuppet~" -Force

dotnet run --project DevTools~/ResolveSharedObject
