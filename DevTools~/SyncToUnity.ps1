dotnet build Resonite~/ResoniteHook/ResoniteHook.sln
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -Path "./ResoPuppet~" -ItemType Directory -Force
Copy-Item -Path "Resonite~/ResoniteHook/ResoPuppetSchema/bin/*" -Destination "./Managed" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/*.dll" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/*.exe" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/*.pdb" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/*.json" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/*.dll" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/*.pdb" -Destination "./ResoPuppet~" -Recurse -Force
Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/*.so*" -Destination "./ResoPuppet~" -Recurse -Force

if (Test-Path "Resonite~/ResoniteHook/Launcher/bin/Launcher"){ Copy-Item -Path "Resonite~/ResoniteHook/Launcher/bin/Launcher" -Destination "./ResoPuppet~" -Force }
if (Test-Path "Resonite~/ResoniteHook/Puppeteer/bin/Puppeteer"){ Copy-Item -Path "Resonite~/ResoniteHook/Puppeteer/bin/Puppeteer" -Destination "./ResoPuppet~" -Force }
