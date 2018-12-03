using Newtonsoft.Json;
using StampyCommon.Models;
using System.Collections.Generic;

namespace StampyCommon
{
    public sealed class CloudStampyParameters
    {
        public string RequestId { get; set; }
        public string JobId { get; set; }
        public StampyJobType JobType { get; set; }
        public string BuildPath { get; set; }
        public string DpkPath { get; set; }
        public string DeploymentTemplate { get; set; }
        public string CloudName { get; set; }
        public List<List<string>> TestCategories { get; set; }
        public string ExpiryDate { get; set; }
        public Status FlowStatus { get; set; }
        internal string HostingPath
        {
            get
            {
                return BuildPath + @"\Hosting";
            }
        }

        public CloudStampyParameters()
        {
            TestCategories = new List<List<string>>();           
        }

        public override string ToString()
        {
            var s = $"JobType: {JobType.ToString()} BuildPath: {BuildPath}";
            return s;
        }

        public CloudStampyParameters Copy()
        {
            var tmp = new CloudStampyParameters
            {
                BuildPath = this.BuildPath,
                CloudName = this.CloudName,
                DeploymentTemplate = this.DeploymentTemplate,
                DpkPath = this.DpkPath,
                JobId = this.JobId,
                JobType = this.JobType,
                RequestId = this.RequestId,
                TestCategories = this.TestCategories,
                ExpiryDate = this.ExpiryDate,
                FlowStatus = this.FlowStatus
            };

            return tmp;
        }

        public string ToJsonString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
