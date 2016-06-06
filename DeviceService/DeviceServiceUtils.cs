using System;
using System.Collections.Generic;

namespace IntegrateDrv.DeviceService
{
	public static class DeviceServiceUtils
	{
		public static List<NetworkDeviceService> FilterNetworkDeviceServices(IEnumerable<BaseDeviceService> deviceServices)
		{
			var result = new List<NetworkDeviceService>();
			foreach (var deviceService in deviceServices)
			{
				if (deviceService is NetworkDeviceService)
					result.Add((NetworkDeviceService) deviceService);
			}
			return result;
		}

		public static bool ContainsService(IEnumerable<BaseDeviceService> deviceServices, string serviceNameToFind)
		{
			foreach (var deviceService in deviceServices)
			{
				if (string.Equals(deviceService.ServiceName, serviceNameToFind, StringComparison.InvariantCultureIgnoreCase))
					return true;
			}
			return false;
		}
	}
}
