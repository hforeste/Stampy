using System;

namespace StampyCommon
{
    public interface ILogger
    {
        void WriteInfo(string message);
        void WriteInfo(string source, string message);
        void WriteError(string message, Exception ex = null);
        void WriteError(string source, string message, Exception ex = null);
    }
}
