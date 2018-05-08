using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    public class StampyResultsLogger : KustoLogger, IStampyResultsLogger
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
                        new KustoColumnMapping { ColumnName = "ExceptionType", ColumnNumber = 6, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "ExceptionDetails", ColumnNumber = 7, DataType = "string" }
                    };
                }

                return _mappings;
            }
        }

        public void WriteResult(CloudStampyParameters parameters, string status, int jobDurationMinutes, Exception ex)
        {
            WriteEvent(DateTime.UtcNow, parameters.RequestId, parameters.JobId, parameters.JobType, status, jobDurationMinutes, ex != null ? ex.GetType().ToString() : "", ex != null ? ex.ToString() : "");
        }
    }
}
