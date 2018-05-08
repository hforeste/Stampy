using Newtonsoft.Json.Linq;

namespace StampyVmssManagement
{
    public class CapacityStatus
    {
        public int TotalInstances { get => AvailableWorkers + ScaleOutOperationsInFlight; }
        public int AvailableWorkers { get; private set; }
        public int ReservedWorkers { get; private set; }
        public int ScaleOutOperationsInFlight { get; private set; }
        public int ScaleInOperationsInFlight { get; private set; }

        public CapacityStatus(string jsonResponse)
        {
            var response = JObject.Parse(jsonResponse);
            if (response != null)
            {
                foreach (var item in response["virtualMachine"]["statusesSummary"])
                {
                    switch (item.Value<string>("code"))
                    {
                        case "ProvisioningState/succeeded":
                            AvailableWorkers += item.Value<int>("count");
                            break;
                        case "ProvisioningState/creating":
                            ScaleOutOperationsInFlight += item.Value<int>("count");
                            break;
                        case "ProvisioningState/deleting":
                            ScaleInOperationsInFlight += item.Value<int>("count");
                            break;
                    }
                }
            }
        }
    }
}
