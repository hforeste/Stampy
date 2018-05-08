namespace StampyCommon
{
    public interface IConfiguration
    {
        /// <summary>
        /// Storage account that has queues for stampy jobs and etc
        /// </summary>
        string StorageAccountConnectionString { get; }
        string StampyJobPendingQueueName { get; }
        string StampyJobFinishedQueueName { get; }
        string StampyJobResultsFileShareName { get; }
        string KustoClientId { get; }
        string KustoClientSecret { get; }
    }
}
