namespace StampyCommon
{
    public interface ISchedulerContext : IStampyContext
    {
        int MaxRetries { get; }
    }
}
