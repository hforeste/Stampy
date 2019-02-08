using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyVmssManagement
{
    public class ResourceCleanerOperation
    {
        public string ResourceType { get; set; }
        public int Total { get; set; }
        public int TotalScavenged { get; set; }
        public int TotalResourceDeletionFailures { get; set; }
        public int TotalCores { get; set; }
        public string MachineSize { get; set; }

        public override string ToString()
        {
            return $"ResourceType: {ResourceType} Total: {Total} TotalScavenged: {TotalScavenged} TotalResourceDeletionFailures: {TotalResourceDeletionFailures} TotalCores: {TotalCores} MachineSize: {MachineSize}";
        }
    }
}
