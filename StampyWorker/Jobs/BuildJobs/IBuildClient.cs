using System.Threading.Tasks;
using StampyCommon;

namespace StampyWorker.Jobs
{
    interface IBuildClient
    {
        Task<JobResult> ExecuteBuild(CloudStampyParameters p);
        Task<JobResult> ExecuteBuild(string dpkPath = null);
    }
}
