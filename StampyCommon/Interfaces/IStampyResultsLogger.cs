﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon
{
    public interface IStampyResultsLogger
    {
        void WriteResult(CloudStampyParameters parameters, string status, int jobDurationMinutes, Exception ex);
        void WriteJobProgress(string requestId, string jobId, StampyJobType jobType, string status, string jobUri);
    }
}
