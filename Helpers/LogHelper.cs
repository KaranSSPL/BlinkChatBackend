namespace BlinkChatBackend.Helpers;

public static class LogHelper
{
    private static ILoggerFactory _loggerFactory = default!;
    private static ILogger _logger = default!;

    public static void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger(nameof(LogHelper));
    }

    public static void LogError(Exception exception, string message, params object[] args)
    {
        _logger.Log(LogLevel.Error, exception, message, args);
    }
    public static void LogError(string message, params object[] args)
    {
        _logger.Log(LogLevel.Error, message, args);
    }
    public static void LogInformation(string message, params object[] args)
    {
        _logger.Log(LogLevel.Information, message, args);
    }
    public static void LogInformation(Exception exception, string message, params object[] args)
    {
        _logger.Log(LogLevel.Information, exception, message, args);
    }
}
