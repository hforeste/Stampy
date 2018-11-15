using StampyCommon;
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
    internal class ServiceCreationJob : AntaresDeploymentBaseJob
    {
        public ServiceCreationJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs) : base(logger, cloudStampyArgs) { }

        protected override void PreExecute()
        {
            //no op
        }

        protected override List<AntaresDeploymentTask> GetAntaresDeploymentTasks()
        {
            var commands = new List<AntaresDeploymentTask>()
            {
                new AntaresDeploymentTask
                {
                    Name = "Create Private Stamp and Geomaster",
                    Description = string.Empty,
                    AntaresDeploymentExcutableParameters = $"SetupPrivateStampWithGeo {_parameters.CloudName} /SubscriptionId:b27cf603-5c35-4451-a33a-abba1a08c9c2 /VirtualDedicated:true /bvtCapableStamp:true /DefaultLocation:\"Central US\""
                }
            };
            return commands;
        }

        protected override void PostExecute()
        {
            var definitionsPath = $@"\\AntaresDeployment\PublicLockBox\{_parameters.CloudName}\developer.definitions";
            var cloudStampyFirewallRules =
@"
<redefine name=""SqlFirewallAddressRangesList"" value=""131.107.0.0/16;167.220.0.0/16;65.55.188.0/24"" /><redefine name=""FirewallLockdownWhitelistedSources"" value=""131.107.0.0/16;167.220.0.0/16;65.55.188.0/24"" />
";
            if (!TryModifyDefinitions(definitionsPath, cloudStampyFirewallRules))
            {
                throw new Exception("Failed to add cloud stampy firewall rules to developer.definitions file.");
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
