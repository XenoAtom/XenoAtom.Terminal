using System.Globalization;
using XenoAtom.Ansi;
using XenoAtom.Terminal;

Terminal.WriteLine("LogTerminal sample (ANSI log validation for CI)");
Terminal.WriteLine($"Terminal: {Terminal.Capabilities.TerminalName}, Ansi={Terminal.Capabilities.AnsiEnabled}, Color={Terminal.Capabilities.ColorLevel}, Redirected={Terminal.Capabilities.IsOutputRedirected}");
Terminal.WriteLine();

var now = DateTimeOffset.UtcNow;
WriteLog(now, LogLevel.Info, "Startup", "Application started");
WriteLog(now.AddMilliseconds(10), LogLevel.Info, "Config", "Loaded configuration file");
WriteLog(now.AddMilliseconds(20), LogLevel.Warning, "Network", "Transient failure, retrying");
WriteLog(now.AddMilliseconds(30), LogLevel.Error, "Database", "Connection failed (simulated)");
WriteLog(now.AddMilliseconds(40), LogLevel.Info, "Shutdown", "Done");

Terminal.ResetStyle();


static void WriteLog(DateTimeOffset timestamp, LogLevel level, string category, string message)
{
    Terminal.WriteAtomic(w =>
    {
        w.ResetStyle();

        // Timestamp
        w.Foreground(ConsoleColor.DarkGray);
        w.Write("[");
        w.Write(timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
        w.Write("] ");

        // Level
        var levelColor = level switch
        {
            LogLevel.Info => ConsoleColor.Green,
            LogLevel.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };

        w.Foreground(levelColor);
        w.Decorate(AnsiDecorations.Bold);
        w.Write("[");
        w.Write(level.ToString().ToUpperInvariant());
        w.Write("] ");
        w.Undecorate(AnsiDecorations.Bold);

        // Category
        w.Foreground(ConsoleColor.Cyan);
        w.Write("[");
        w.Write(category);
        w.Write("] ");

        // Message
        w.Foreground(AnsiColor.Default);
        w.Write(message);
        w.Write("\n");
    });
}
enum LogLevel
{
    Info,
    Warning,
    Error,
}