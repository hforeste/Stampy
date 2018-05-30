using StampyCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker
{
    internal class KustoLoggingConfiguration : IConfiguration
    {
        public string StorageAccountConnectionString => throw new NotImplementedException();

        public string StampyJobPendingQueueName => throw new NotImplementedException();

        public string StampyJobFinishedQueueName => throw new NotImplementedException();

        public string StampyJobResultsFileShareName => throw new NotImplementedException();

        public string KustoClientId => Environment.GetEnvironmentVariable("KustoClientId");

        public string KustoClientSecret => Environment.GetEnvironmentVariable("KustoSecret");
    }
}
