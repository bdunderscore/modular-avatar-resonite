using System.Reflection;
using System.Runtime.InteropServices;
using FrooxEngine;
using nadena.dev.resonity.engine;
using nadena.dev.resonity.remote.bootstrap;
using NUnit.Framework;

namespace nadena.dev.resonity.remote.puppeteer.tests;

public abstract class TestBase
{
    private string assemblyBase;
    private List<string> dllPaths;
    
    private EngineController _engineController;
    
    [OneTimeSetUp]
    public async Task Setup()
    {
        new Launcher().ConfigurePaths();
        _engineController = new EngineController();
        
        await _engineController.Start();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _engineController.DisposeAsync();
    }
}