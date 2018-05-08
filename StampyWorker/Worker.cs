using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;

namespace StampyWorker
{
    public static class Worker
    {
        [FunctionName("Worker")]
        public static void Run([QueueTrigger("stampy-jobs", Connection = "Stampyvmssmgmt")]string myQueueItem, TraceWriter log)
        {
            log.Info($"C# Queue trigger function processed: {myQueueItem}");
        }
    }
}
