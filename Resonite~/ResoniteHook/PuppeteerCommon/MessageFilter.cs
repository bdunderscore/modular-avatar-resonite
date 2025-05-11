using System.Text.RegularExpressions;

namespace nadena.dev.resonity.remote.puppeteer.logging;

/// <summary>
/// Suppresses noisy FrooxEngine log messages (setting them debug level)
/// </summary>
internal static class MessageFilter
{
    private static readonly Regex FilteredRegex = new Regex(@"^(Steam API failed to initialize|Exception computing UID)");

    public static bool IsFilteredMessage(string message)
    {
        return FilteredRegex.Match(message).Success;
    }
}