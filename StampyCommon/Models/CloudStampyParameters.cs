using Newtonsoft.Json;
using System.Collections.Generic;

namespace StampyCommon
{
    public class CloudStampyParameters
    {
        public string RequestId { get; set; }
        public string JobId { get; set; }
        public StampyJobType JobType { get; set; }
        public string BuildPath { get; set; }
        public string DpkPath { get; set; }
        public string DeploymentTemplate { get; set; }
        public string CloudName { get; set; }
        public List<string> TestCategories { get; set; }
        internal string HostingPath
        {
            get
            {
                return BuildPath + @"\Hosting";
            }
        }

        public CloudStampyParameters()
        {
            TestCategories = new List<string>();           
        }

        public override string ToString()
        {
            var s = $"JobType: {JobType.ToString()} BuildPath: {BuildPath}";
            return s;
        }

        public string ToJsonString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
