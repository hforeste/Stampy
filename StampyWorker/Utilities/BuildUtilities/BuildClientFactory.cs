using StampyCommon;
using StampyWorker.Jobs;

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
