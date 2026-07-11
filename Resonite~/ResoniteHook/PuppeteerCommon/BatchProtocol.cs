namespace nadena.dev.resonity.remote.puppeteer.logging;

/// <summary>
/// Sentinel prefix used to distinguish structured build-status lines from ordinary log lines on
/// stdout. The Unity-side runner must use the exact same prefix (see
/// Editor/ResoniteBackendRunner.cs).
/// </summary>
public static class BatchProtocol
{
    public const string Prefix = "MA-RESO ";

    public const string Progress = Prefix + "PROGRESS ";
    public const string Error = Prefix + "ERROR ";
    public const string StructuredError = Prefix + "STRUCTURED_ERROR ";
    public const string Done = Prefix + "DONE";
}
