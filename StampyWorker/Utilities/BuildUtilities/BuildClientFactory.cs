using StampyCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker.Utilities
{
    internal class BuildClientFactory
    {
        public static IBuildClient GetBuildClient(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            return new LabMachineBuildClient(logger, cloudStampyArgs);
        }
    }
}
