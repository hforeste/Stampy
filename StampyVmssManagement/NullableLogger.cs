using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace StampyVmssManagement
{
    internal class NullableLogger : TraceWriter
    {
        public NullableLogger() : base(System.Diagnostics.TraceLevel.Off) { }
        public override void Trace(TraceEvent traceEvent)
        {
            //no op
        }
    }
}
