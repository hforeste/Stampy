namespace StampyCommon
{
    public interface IStampyContext
    {
        IConfiguration Configuration { get; }
        ILogger Logger { get; }
        ICloudStampyLogger KustoLogger { get; }
    }
}
