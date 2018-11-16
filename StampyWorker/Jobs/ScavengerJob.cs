using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StampyCommon;
using StampyCommon.Models;

namespace StampyWorker.Jobs
{
    internal class ScavengerJob : AntaresDeploymentBaseJob
    {
        private string _subscriptionId;

        private List<AntaresDeploymentTask> _commands;
        public ScavengerJob(ICloudStampyLogger logger, CloudStampyParameters cloudStampyArgs) : base(logger, cloudStampyArgs)
        {
        }

        protected override List<AntaresDeploymentTask> GetAntaresDeploymentTasks()
        {
            return new List<AntaresDeploymentTask>
            {
                new AntaresDeploymentTask
                {
                    Name = "Delete Resources",
                    Description = "Remove all resources corresponding to this cloud service",
                    AntaresDeploymentExcutableParameters = $@"DeletePrivateEnvironment {Parameters.CloudName} /SubscriptionId:b27cf603-5c35-4451-a33a-abba1a08c9c2 /deleteLockBoxFiles:true /deleteSecretStore:true"
                }
            };
        }

        protected override void PostExecute()
        {
            //no op
        }

        protected override void PreExecute()
        {
            var branchDirectory = @"\\reddog\builds\branches\rd_websites_n_release\";
            var nBuild = Path.Combine(branchDirectory, GetLatestBuild(branchDirectory), "bin");
            Parameters.BuildPath = nBuild;
        }

        private string GetLatestBuild(string branch)
        {
            var di = new DirectoryInfo(branch);
            List<DirectoryInfo> rdWebsites = di.EnumerateDirectories()
                .OrderBy(d => d.CreationTime)
                .Reverse()
                .ToList();

            var dirs = new Dictionary<int, DirectoryInfo>();
            foreach (var dir in rdWebsites)
            {
                string name = dir.Name;
                string[] number = name.Split('.');
                if (number.Length > 2)
                {
                    if (!dirs.ContainsKey(Int32.Parse(number[3])))
                        dirs.Add(Int32.Parse(number[3]), dir);
                    else

                        Console.WriteLine(number[3]);
                }
            }

            var sortedList = dirs.Keys.OrderBy(d => d)
                .Reverse()
                .ToList();

            if (DateTime.Now.Subtract(dirs[sortedList[0]].CreationTime).TotalMinutes > 20)
                return dirs[sortedList[0]].Name;

            Console.WriteLine("Folder is there but not ready");
            return dirs[sortedList[1]].Name;
        }
    }
}
