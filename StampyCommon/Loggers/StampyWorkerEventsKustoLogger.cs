using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace StampyCommon.Loggers
{
    public class StampyWorkerEventsKustoLogger : Logger, ICloudStampyLogger
    {
        private string _workerIpAddress;
        private List<KustoColumnMapping> _mappings;

        public StampyWorkerEventsKustoLogger(IConfiguration configuration) : base(configuration, "StampyWorkerEvents")
        {
            SetupWorkerName();
        }

        public StampyWorkerEventsKustoLogger(IConfiguration configuration, string tableName) : base(configuration, tableName)
        {
            SetupWorkerName();
        }

        protected override List<KustoColumnMapping> KustoColumnMappings
        {
            get
            {
                if (_mappings == null)
                {
                    _mappings = new List<KustoColumnMapping>
                    {
                        new KustoColumnMapping { ColumnName = "TimeStamp", ColumnNumber = 0, DataType = "datetime" },
                        new KustoColumnMapping { ColumnName = "RequestId", ColumnNumber = 1, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "JobId", ColumnNumber = 2, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "IpAddress", ColumnNumber = 3, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "Source", ColumnNumber = 4, DataType = "string"},
                        new KustoColumnMapping { ColumnName = "Message", ColumnNumber = 5, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "ExceptionType", ColumnNumber = 6, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "ExceptionDetails", ColumnNumber = 7, DataType = "string" }
                    };
                }

                return _mappings;
            }
        }

        private void Write(string requestId, string jobId, string source, string message, Exception ex)
        {
            WriteEvent(DateTime.UtcNow, requestId, jobId, _workerIpAddress, source, message, ex != null ? ex.GetType().ToString() : string.Empty, ex != null ? ex.ToString() : string.Empty);
        }

        public override void WriteError(string source, string message, Exception ex = null)
        {
            Write(null, null, source, message, ex);
        }
        public override void WriteError(string message, Exception ex = null)
        {
            Write(null, null, null, message, ex);
        }

        public override void WriteInfo(string source, string message)
        {
            Write(null, null, source, message, null);
        }

        public override void WriteInfo(string message)
        {
            Write(null, null, null, message, null);
        }

        private void SetupWorkerName()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                Console.WriteLine(ip);
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    _workerIpAddress = ip.ToString();
                }
            }
        }

        public void WriteInfo(CloudStampyParameters stampyParameters, string message)
        {
            Write(stampyParameters.RequestId, stampyParameters.JobId, null, message, null);
        }

        public void WriteInfo(CloudStampyParameters stampyParameters, string source, string message)
        {
            Write(stampyParameters.RequestId, stampyParameters.JobId, source, message, null);
        }

        public void WriteError(CloudStampyParameters stampyParameters, string message, Exception ex = null)
        {
            Write(stampyParameters.RequestId, stampyParameters.JobId, null, message, ex);
        }

        public void WriteError(CloudStampyParameters stampyParameters, string source, string message, Exception ex = null)
        {
            Write(stampyParameters.RequestId, stampyParameters.JobId, source, message, ex);
        }
    }
}
