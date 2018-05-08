using System;

namespace StampyCommon
{
    public interface IStampyClientLogger
    {
        void WriteRequest(StampyClientRequest request);
        void WriteInfo(StampyClientRequest request, string message);
        void WriteInfo(StampyClientRequest request, string source, string message);
        void WriteError(StampyClientRequest request, string message, Exception ex = null);
        void WriteError(StampyClientRequest request, string source, string message, Exception ex = null);
    }
}
