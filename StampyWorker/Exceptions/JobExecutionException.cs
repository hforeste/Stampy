using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Exceptions
{
    /// <summary>
    /// Job exception
    /// </summary>
    internal class JobExecutionException : Exception
    {
        public string Name { get; }
        public JobExecutionException(string jobName, string message) : base(message) { Name = jobName; }
        public JobExecutionException(string jobName, string message, Exception innerException) : base(message, innerException) { Name = jobName; }
    }
}
