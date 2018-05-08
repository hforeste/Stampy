using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    public class StampyClientRequestLogger : KustoLogger, IStampyClientLogger
    {
        private List<KustoColumnMapping> _mappings;
        public StampyClientRequestLogger(IConfiguration configuration) : base(configuration, "StampyClientRequests")
        {
        }

        protected override List<KustoColumnMapping> KustoColumnMappings
        {
            get
            {
                if (_mappings == null)
                {
                    _mappings = new List<KustoColumnMapping>
                    {
                        new KustoColumnMapping { ColumnName = "TimeStamp", ColumnNumber = 0, DataType = "datetime" },
                        new KustoColumnMapping { ColumnName = "RequestId", ColumnNumber = 1, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "FlowId", ColumnNumber = 2, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "JobTypes", ColumnNumber = 3, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "BuildPath", ColumnNumber = 4, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "DpkPath", ColumnNumber = 5, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "CloudServiceNames", ColumnNumber = 6, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "DeploymentTemplatePerCloudService", ColumnNumber = 7, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "TestCategories", ColumnNumber = 8, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "Client", ColumnNumber = 9, DataType = "string" },
                        new KustoColumnMapping { ColumnName = "User", ColumnNumber = 10, DataType = "string" }
                    };
                }

                return _mappings;
            }
        }

        public void WriteRequest(StampyClientRequest request)
        {
            var deploymentTemplatesString = string.Join("|", request.DeploymentTemplates);
            var cloudNamesString = string.Join("|", request.CloudNames);
            var testCategoriesString = string.Join("|", request.TestCategories);
            var jobTypes = string.Join("|", request.JobTypes.ToString().Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));

            var message = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}", DateTime.UtcNow.ToString(), request.RequestId, request.FlowId, jobTypes, 
                request.BuildPath, request.DpkPath, cloudNamesString, deploymentTemplatesString, testCategoriesString, request.Client, request.EndUserAlias);
            WriteEvent(message);
        }

        public void WriteError(StampyClientRequest request, string message, Exception ex = null)
        {
            throw new NotImplementedException();
        }

        public void WriteError(StampyClientRequest request, string source, string message, Exception ex = null)
        {
            throw new NotImplementedException();
        }

        public void WriteInfo(StampyClientRequest request, string message)
        {
            throw new NotImplementedException();
        }

        public void WriteInfo(StampyClientRequest request, string source, string message)
        {
            throw new NotImplementedException();
        }
    }
}
