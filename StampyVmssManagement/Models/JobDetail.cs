using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;

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
        public Dictionary<string, string> Details { get; set; }

        public JobDetail(DataRow jobMetaData, DataTable jobDetails, DataRow request)
        {
            DateTime.TryParse(jobMetaData["StartTime"].ToString(), out DateTime startTime);
            DateTime.TryParse(jobMetaData["EndTime"].ToString(), out DateTime endTime);
            int.TryParse(jobMetaData["JobDurationInMinutes"].ToString(), out int jobDurationInMinutes);

            Id = (string)jobMetaData["JobId"];
            Type = (string)jobMetaData["JobType"];
            Status = (string)jobMetaData["Status"];
            StartTime = startTime;
            EndTime = endTime;
            ExceptionType = jobMetaData["ExceptionType"].ToString();
            ExceptionDetails = jobMetaData["ExceptionDetails"].ToString();
            JobDurationInMinutes = jobDurationInMinutes;
            ReportUri = new Uri(jobMetaData["ReportUri"].ToString());
            Details = new Dictionary<string, string>();

            switch((StampyJobType)Enum.Parse(typeof(StampyJobType), Type))
            {
                case StampyJobType.Build:
                    SetBuildDetails(jobDetails);
                    break;
                case StampyJobType.CreateService:
                    SetCreateServiceDetails(jobDetails, request);
                    break;
                case StampyJobType.Deploy:
                    SetDeploymentDetails(jobDetails);
                    break;
            }
        }

        private void SetBuildDetails(DataTable jobDetails)
        {
            foreach (DataRow row in jobDetails.Rows)
            {
                if (!Details.ContainsKey(Constants.MachineName))
                {
                    if (!row.IsNull(Constants.MachineColumn))
                    {
                        Details.Add(Constants.MachineName, row[Constants.MachineColumn].ToString());
                    }
                }

                if (!Details.ContainsKey(Constants.Build))
                {
                    if (!row.IsNull(Constants.MessageColumn) && row[Constants.MessageColumn].ToString().StartsWith("Finished executing build"))
                    {
                        Details.Add(Constants.Build, row[Constants.MessageColumn].ToString().Split(null).Last());
                    }
                }
            }
        }

        private void SetCreateServiceDetails(DataTable jobDetails, DataRow request)
        {
            foreach (DataRow row in jobDetails.Rows)
            {
                if (!Details.ContainsKey(Constants.MachineName))
                {
                    if (!row.IsNull(Constants.MachineColumn))
                    {
                        Details.Add(Constants.MachineName, row[Constants.MachineColumn].ToString());
                    }
                }
            }

            if (!request.IsNull(Constants.CloudNamesColumn))
            {
                var cloudNames = request[Constants.CloudNamesColumn].ToString().Split(new char[] { '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < cloudNames.Length; i++)
                {
                    Details.Add($"cloudservice{i + 1}", cloudNames[i]);
                }
            }
        }

        private void SetDeploymentDetails(DataTable jobDetails)
        {
            foreach (DataRow row in jobDetails.Rows)
            {
                if (!Details.ContainsKey(Constants.MachineName))
                {
                    if (!row.IsNull(Constants.MachineColumn))
                    {
                        Details.Add(Constants.MachineName, row[Constants.MachineColumn].ToString());
                    }
                }

                if (!Details.ContainsKey(Constants.DeploymentCommand))
                {
                    if (!row.IsNull(Constants.MessageColumn) && row[Constants.MessageColumn].ToString().Contains("DeployConsole.exe"))
                    {
                        Details.Add(Constants.DeploymentCommand, row[Constants.MessageColumn].ToString());
                    }
                }
            }
        }
    }
}
