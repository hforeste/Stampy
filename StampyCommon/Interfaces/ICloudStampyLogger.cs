using System;

namespace StampyCommon
{
    public interface ICloudStampyLogger
    {
        void WriteInfo(CloudStampyParameters stampyParameters, string message);
        void WriteInfo(CloudStampyParameters stampyParameters, string source, string message);
        void WriteError(CloudStampyParameters stampyParameters, string message, Exception ex = null);
        void WriteError(CloudStampyParameters stampyParameters, string source, string message, Exception ex = null);
    }
}
