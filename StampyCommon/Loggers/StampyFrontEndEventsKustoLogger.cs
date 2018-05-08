namespace StampyCommon.Loggers
{
    /*
     * Since StampyFrontEndEvents and StampyWorkerEvents share the same columns, inherit from the stampyworkerevents class 
     */
    public class StampyFrontEndEventsKustoLogger : StampyWorkerEventsKustoLogger
    {
        public StampyFrontEndEventsKustoLogger(IConfiguration configuration) : base(configuration, "StampyFrontEndEvents"){

        }
    }
}
