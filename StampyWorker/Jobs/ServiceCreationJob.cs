using StampyCommon;
using StampyCommon.Loggers;
using StampyCommon.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace StampyWorker.Jobs
{
    internal class ServiceCreationJob : IJob
    {
        ICloudStampyLogger _logger;
        CloudStampyParameters _parameters;
        private StringBuilder _statusMessageBuilder;
        private JobResult _result;
        private AzureFileLogger _azureFilesWriter;
        private List<Task> _azureLogsWriterUnfinishedJobs;
        private const string LOG_FILE_NAME = "createservice.log";

        public ServiceCreationJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs)
        {
            _logger = logger;
            _result = new JobResult();
            _parameters = cloudStampyArgs;
            _statusMessageBuilder = new StringBuilder();
            _azureFilesWriter = new AzureFileLogger(new LoggingConfiguration(), cloudStampyArgs, logger);
            _azureLogsWriterUnfinishedJobs = new List<Task>();
        }

        public Status JobStatus { get; set; }
        public string ReportUri
        {
            get
            {
                _azureFilesWriter.LogUrls.TryGetValue(LOG_FILE_NAME, out string url);
                return url;
            }
            set
            { }
        }

        public Task<bool> Cancel()
        {
            return Task.FromResult(true);
        }

        public async Task<JobResult> Execute()
        {
            if (!File.Exists(AntaresDeploymentExecutablePath))
            {
                var ex = new FileNotFoundException("Cannot find file", Path.GetFileName(AntaresDeploymentExecutablePath));
                _logger.WriteError(_parameters, "Cannot find file", ex);
                throw ex;
            }

            var processStartInfo = new ProcessStartInfo();
            processStartInfo.FileName = AntaresDeploymentExecutablePath;
            processStartInfo.Arguments = $"SetupPrivateStampWithGeo {_parameters.CloudName} /SubscriptionId:b27cf603-5c35-4451-a33a-abba1a08c9c2 /VirtualDedicated:true /bvtCapableStamp:true /DefaultLocation:\"Central US\"";
            processStartInfo.UseShellExecute = false;
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;

            _logger.WriteInfo(_parameters, $"Start {processStartInfo.FileName} {processStartInfo.Arguments}");

            using (var createProcess = Process.Start(processStartInfo))
            {
                createProcess.BeginErrorReadLine();
                createProcess.BeginOutputReadLine();
                createProcess.OutputDataReceived += new DataReceivedEventHandler(OutputReceived);
                createProcess.ErrorDataReceived += new DataReceivedEventHandler(ErrorReceived);
                createProcess.WaitForExit();
            }

            var definitionsPath = $@"\\AntaresDeployment\PublicLockBox\{_parameters.CloudName}\developer.definitions";
            var cloudStampyFirewallRules =
@"
<redefine name=""SqlFirewallAddressRangesList"" value=""131.107.0.0/16;167.220.0.0/16;65.55.188.0/24"" /><redefine name=""FirewallLockdownWhitelistedSources"" value=""131.107.0.0/16;167.220.0.0/16;65.55.188.0/24"" />
";
            if (!TryModifyDefinitions(definitionsPath, cloudStampyFirewallRules))
            {
                _statusMessageBuilder.AppendLine("Failed to add cloud stampy firewall rules to developer.definitions file.");
                _result.JobStatus = Status.Failed;
            }

            _result.Message = _statusMessageBuilder.ToString();
            _result.JobStatus = JobStatus = _result.JobStatus == Status.None ? Status.Passed : _result.JobStatus;
            await Task.WhenAll(_azureLogsWriterUnfinishedJobs);
            return _result;
        }

        private void OutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _azureLogsWriterUnfinishedJobs.Add(_azureFilesWriter.CreateLogIfNotExistAppendAsync(LOG_FILE_NAME, e.Data));
                if (JobStatus == default(Status))
                {
                    JobStatus = Status.InProgress;
                }

                _logger.WriteInfo(_parameters, e.Data);
                if (e.Data.Contains("<ERROR>") || e.Data.Contains("<Exception>") || e.Data.Contains("Error:"))
                {
                    _statusMessageBuilder.AppendLine(e.Data);
                    _result.JobStatus = JobStatus = Status.Failed;
                }
            }
        }

        private void ErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _azureLogsWriterUnfinishedJobs.Add(_azureFilesWriter.CreateLogIfNotExistAppendAsync("createservice.log", e.Data));
                _statusMessageBuilder.AppendLine(e.Data);
            }
        }

        private string AntaresDeploymentExecutablePath
        {
            get
            {
                return Path.Combine(_parameters.BuildPath, @"hosting\Azure\RDTools\Tools\Antares\AntaresDeployment.exe");
            }
        }

        private bool TryModifyDefinitions(string definitionPath, string additionalDefinitions)
        {

            try
            {
                string xmlTemplate = (@"<?xml version=""1.0"" encoding=""utf-8""?>
<definitions xmlns=""http://schemas.microsoft.com/configdefinitions/"">
<import path=""private.definitions"" />
{0}</definitions>");

                var modifyXML = string.Format(xmlTemplate, additionalDefinitions);

                var xmlreader2 = XmlReader.Create(new StringReader(modifyXML));
                var origianlDoc = new XmlDocument();
                origianlDoc.Load(definitionPath);
                var modifiedDoc = new XmlDocument();
                modifiedDoc.Load(xmlreader2);

                var original = GetHashTable(origianlDoc);
                var modified = GetHashTable(modifiedDoc);

                Merge(original, modified);

                var xml = "";
                foreach (var name in original.Keys)
                {
                    xml += string.Format(@"<redefine name=""{0}"" value=""{1}"" />", name, original[name]);
                    xml += string.Format("\r\n");
                }

                var text = string.Format(xmlTemplate, xml);
                File.WriteAllText(definitionPath, text);
                return true;
            }
            catch (Exception ex)
            {
                _logger.WriteError(_parameters, "Failed to modify definitions file", ex);
                return false;
            }


        }

        private static void Merge<TKey, TValue>(IDictionary<TKey, TValue> first, IDictionary<TKey, TValue> second)
        {
            if (second == null || first == null) return;
            foreach (var item in second)
                if (!first.ContainsKey(item.Key))
                    first.Add(item.Key, item.Value);
                else
                    first[item.Key] = item.Value;
        }

        private Dictionary<string, string> GetHashTable(XmlDocument doc)
        {
            var table = new Dictionary<string, string>();
            XmlNodeList scenarios = doc.GetElementsByTagName("redefine");
            foreach (XmlLinkedNode scenarioNode in scenarios)
            {
                var name = scenarioNode.Attributes["name"].Value;
                var value = scenarioNode.Attributes["value"].Value;
                if (!table.ContainsKey(name))
                    table.Add(name, value);
            }
            return table;
        }
    }
}
