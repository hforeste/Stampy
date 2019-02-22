using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;

namespace StampyVmssManagement
{
    public class KustoConfiguration : IConfiguration
    {
        public string StorageAccountConnectionString => throw new NotImplementedException();

        public string StampyJobPendingQueueName => throw new NotImplementedException();

        public string StampyJobFinishedQueueName => throw new NotImplementedException();

        public string StampyJobResultsFileShareName => throw new NotImplementedException();

        public string KustoClientId => Environment.GetEnvironmentVariable("KustoClientId");

        public string KustoClientSecret => Environment.GetEnvironmentVariable("KustoSecret");

        public string KustoDatabase => Environment.GetEnvironmentVariable("SourceKustoDatabase");

        public bool IsProduction => true;
    }
}
