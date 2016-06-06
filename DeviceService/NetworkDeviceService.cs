namespace IntegrateDrv.DeviceService
{
	public class NetworkDeviceService : BaseDeviceService
	{
		private readonly string _netCfgInstanceID = string.Empty;

		public NetworkDeviceService(string deviceDescription, string serviceName, string serviceDisplayName,
			string serviceGroup, int serviceType, int errorControl, string fileName, string imagePath, string netCfgInstanceID)
			: base(
				deviceDescription, serviceName, serviceDisplayName, serviceGroup, serviceType, errorControl, fileName, imagePath)
		{
			_netCfgInstanceID = netCfgInstanceID;
		}

		public string NetCfgInstanceID
		{
			get { return _netCfgInstanceID; }
		}
	}
}