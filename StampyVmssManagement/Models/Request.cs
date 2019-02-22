using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyVmssManagement.Models
{
    public class Request
    {
        public DateTime RequestTimeStamp { get; set; }
        public string User { get; set; }
        public string Id { get; set; }
        public string Client { get; set; }
        public string Branch { get; set; }
        public string[] JobTypes { get; set; }
    }
}
