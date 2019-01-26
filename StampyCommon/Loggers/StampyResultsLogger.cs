using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    public class StampyResultsLogger : Logger, IStampyResultsLogger
    {
        private List<KustoColumnMapping> _mappings;
        public StampyResultsLogger(IConfiguration configuration) : base(configuration, "StampyResults")
        {
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
                        new KustoColumnMapping { ColumnName = "JobType", ColumnNumber = 3, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "Status", ColumnNumber = 4, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "JobDurationMinutes", ColumnNumber = 5, DataType = "int" },
                        new KustoColumnMapping { ColumnName = "ReportUri", ColumnNumber = 6, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "ExceptionType", ColumnNumber = 7, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "ExceptionDetails", ColumnNumber = 8, DataType = "string" }
                    };
                }

                return _mappings;
            }
        }

        public void WriteJobProgress(string requestId, string jobId, StampyJobType jobType, string status, string jobUri)
        {
            WriteEvent(DateTime.UtcNow, requestId, jobId, jobType, status, null, jobUri, null, null);
        }

        public void WriteResult(string requestId, string jobId, StampyJobType jobType, string status, int jobDurationMinutes, string jobUri, Exception ex)
        {
            WriteEvent(DateTime.UtcNow, requestId, jobId, jobType, status, jobDurationMinutes, jobUri, ex != null ? ex.GetType().ToString() : "", ex != null ? ex.ToString() : "");
        }
    }
}
