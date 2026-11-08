using MelonLoader;

namespace Backpack;

/// <summary>
/// A wrapper around whatever logging system is used.
/// Allows for changing the logging method without changing the code.
/// </summary>
public static class Logger
{
    private static volatile bool _diagnosticLoggingEnabled;

    public static bool DiagnosticLoggingEnabled => _diagnosticLoggingEnabled;

    public static void SetDiagnosticLogging(bool enabled)
    {
        _diagnosticLoggingEnabled = enabled;
    }

    public static void Debug(string message, params object[] args)
    {
        Diagnostic(message, args);
    }

    public static void Info(string message, params object[] args)
    {
        Melon<BackpackMod>.Logger.Msg(message, args);
    }

    public static void Diagnostic(string message, params object[] args)
    {
        if (_diagnosticLoggingEnabled)
            Melon<BackpackMod>.Logger.Msg(ConsoleColor.DarkMagenta, message, args);
    }

    public static void Diagnostic(Func<string> messageFactory)
    {
        if (_diagnosticLoggingEnabled)
            Melon<BackpackMod>.Logger.Msg(ConsoleColor.DarkMagenta, messageFactory());
    }

    public static void Warning(string message, params object[] args)
    {
        Melon<BackpackMod>.Logger.Warning(message, args);
    }

    public static void Error(string message, params object[] args)
    {
        Melon<BackpackMod>.Logger.Error(message, args);
    }

    public static void Error(Exception exception, string message, params object[] args)
    {
        Melon<BackpackMod>.Logger.Error(string.Format(message, args), exception);
    }
}
