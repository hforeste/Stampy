using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyVmssManagement.Models
{
    public class JobDetail
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public Uri ReportUri { get; set; }
        public double JobDurationInMinutes { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionDetails { get; set; }
    }
}
