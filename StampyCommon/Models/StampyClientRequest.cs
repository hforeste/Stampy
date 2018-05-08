using Newtonsoft.Json;
using System.Collections.Generic;

namespace StampyCommon
{
    public class StampyClientRequest
    {
        public string RequestId { get; set; }
        public string FlowId { get; set; }
        public StampyJobType JobTypes { get; set; }
        public string BuildPath { get; set; }
        public string DpkPath { get; set; }
        public List<string> DeploymentTemplates { get; set; }
        public List<string> CloudNames { get; set; }
        public List<string> TestCategories { get; set; }
        public string Client { get; set; }
        public string EndUserAlias { get; set; }

        public StampyClientRequest()
        {
            CloudNames = new List<string>();
            DeploymentTemplates = new List<string>();
            TestCategories = new List<string>();
        }

        public string ToJsonString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
