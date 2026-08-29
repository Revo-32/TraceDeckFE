namespace TraceDeckFE.Services;

public interface ITraceLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}
