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
        public static IJob GetJob(ICloudStampyLogger logger, CloudStampyParameters args)
        {
            IJob job = null;

            switch (args.JobType)
            {
                case StampyJobType.None:
                    break;
                case StampyJobType.CreateService:
                    job = new ServiceCreationJob(logger, args);
                    break;
                case StampyJobType.Build:
                    break;
                case StampyJobType.Deploy:
                    job = new DeploymentJob(logger, args);
                    break;
                case StampyJobType.Test:
                    break;
                default:
                    break;
            }

            return job;
        }
    }
}
