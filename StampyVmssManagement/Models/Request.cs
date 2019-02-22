using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StampyCommon;

namespace StampyVmssManagement.Models
{
    public class Request
    {
        public DateTime RequestTimeStamp { get; set; }
        public string User { get; set; }
        public string Id { get; set; }
        public string Client { get; set; }
        public string BuildFileShare { get; set; }
        public string DpkPath { get; set; }
        public string Branch { get; set; }
        public List<string> JobTypes { get; set; }
        public string TestCategories { get; set; }
        public Dictionary<string ,string> CloudDeployments { get; set; }
    }
}
