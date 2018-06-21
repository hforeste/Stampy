using System;

namespace StampyCommon
{
    [Flags]
    public enum StampyJobType
    {
        None = 0,
        Build = 1,
        Deploy = 2,
        Test = 4,
        CreateService = 8,
        RemoveResources = 16
    }
}
