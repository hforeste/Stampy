using StampyCommon;
using StampyWorker.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyWorker
{
    internal class JobFactory
    {
        public static IJob GetJob(ICloudStampyLogger logger, CloudStampyParameters args, StampyJobType requestedJobType)
        {
            IJob job = null;

            switch (requestedJobType)
            {
                case StampyJobType.None:
                    break;
                case StampyJobType.CreateService:
                    job = new ServiceCreationJob(logger, args);
                    break;
                case StampyJobType.Build:
                    job = new BuildJob(logger, args);
                    break;
                case StampyJobType.Deploy:
                    job = new DeploymentJob(logger, args);
                    break;
                case StampyJobType.Test:
                    job = new TestJob(logger, args);
                    break;
                default:
                    break;
            }

            return job;
        }
    }
}
