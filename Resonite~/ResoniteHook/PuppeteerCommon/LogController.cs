#nullable enable

namespace nadena.dev.resonity.remote.puppeteer.logging;

public static class LogController
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
    
    public struct Message
    {
        public LogLevel Level;
        public string Text;
        public DateTime Time;
    }

    private static object _lock = new();
    private static TextWriter? LogFile = System.Console.Out;
    private static LogListener? LogListener = null;
    
    public static void OpenLogfile(string logPath)
    {
        if (LogFile != null && LogFile != System.Console.Out)
        {
            LogFile.Close();
        }
        
        try
        {
            LogFile = new StreamWriter(logPath, false);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to open log file: {e}");
            LogFile = System.Console.Out;
        }
    }

    public static void Flush()
    {
        LogFile.Flush();
    }
    
    public static void Log(LogLevel level, string message)
    {
        if (MessageFilter.IsFilteredMessage(message))
        {
            level = LogLevel.Debug;
        }
        
        var logEntry = new Message
        {
            Level = level,
            Text = message,
            Time = DateTime.UtcNow,
        };
        
        var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        LogListener?.Enqueue(logEntry);
        
        if (LogFile != null)
        {
            LogFile.WriteLine(logMessage);
            LogFile.Flush();
        }
    }
    
    public static LogListener StartLogListening()
    {
        lock (_lock) {
            if (LogListener != null)
            {
                throw new InvalidOperationException("Log listener already exists");
            }
            var listener = new LogListener();
            LogListener = listener;
        
            return listener;
        }
    }
    
    internal static void StopLogListening(LogListener listener)
    {
        lock (_lock)
        {
            if (LogListener == listener) LogListener = null;            
        }
    }
}