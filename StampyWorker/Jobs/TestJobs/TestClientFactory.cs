using StampyCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Jobs
{
    internal class TestClientFactory
    {
        public static ITestClient GetTestClient(ICloudStampyLogger logger, CloudStampyParameters args)
        {
            return new LabMachineTestClient(logger, args);
        }
    }
}
