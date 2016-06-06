using System;
using System.Collections.Generic;
using IntegrateDrv.ExportedRegistry;
using IntegrateDrv.Integrators;
using IntegrateDrv.PNPDriver;
using IntegrateDrv.Utilities.Registry;
using IntegrateDrv.Utilities.Strings;

namespace IntegrateDrv.HardwareDetectors
{
	public static class PNPExportedHardwareDetector
	{
		public static List<string> DetectMatchingExportedHardware(string path, PNPDriverDirectory driverDirectory, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var devices = driverDirectory.ListDevices(architectureIdentifier, minorOSVersion, productType);
			var driverHardwareID = new List<string>();
			foreach (var device in devices)
				driverHardwareID.Add(device.Key);
			return DetectMatchingExportedHardware(path, driverHardwareID);
		}

		// driverHardwareID can be different than the actuall matching hardware ID,
		// for example: driver hardware ID can be VEN_8086&DEV_100F, while the actuall hardware may present VEN_8086&DEV_100F&SUBSYS...
		/// <returns>List of driverHardwareID that match list of hardware</returns>
		private static List<string> DetectMatchingExportedHardware(string path, IEnumerable<string> driverHardwareIDs)
		{
			var localHardwareIDs = GetExportedHardwareCompatibleIDs(path);
			var result = new List<string>();
			foreach (var driverHardwareID in driverHardwareIDs)
			{
				if (StringUtils.ContainsCaseInsensitive(localHardwareIDs, driverHardwareID))
					// localHardwareIDs is sometimes upcased by Windows
					result.Add(driverHardwareID);
			}
			return result;
		}

		/// <param name="path"></param>
		/// <param name="hardwareID">enumerator-specific-device-id</param>
		/// <param name="deviceID"></param>
		public static string DetectExportedDeviceInstanceID(string path, string hardwareID, out string deviceID)
		{
			Console.WriteLine("Searching for '" + hardwareID + "' in " + path);
			deviceID = string.Empty; // sometimes the device presents longer hardware ID than the one specified in the driver

			var enumerator = PNPDriverIntegratorUtils.GetEnumeratorNameFromHardwareID(hardwareID);
			if (enumerator == "*")
				return string.Empty; // unsupported enumerator;

			var deviceInstanceID = string.Empty;
			var hiveKey = new ExportedRegistryINI(path).LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum" + enumerator);

			foreach (var deviceKeyName in hiveKey.GetSubKeyNames())
			{
				var deviceKey = hiveKey.OpenSubKey(deviceKeyName);
				if (deviceKey != null)
				{
					foreach (var instanceKeyName in deviceKey.GetSubKeyNames())
					{
						var instanceKey = deviceKey.OpenSubKey(instanceKeyName);
						if (instanceKey != null)
						{
							var compatibleIDsEntry = instanceKey.GetValue("CompatibleIDs", new string[0]);
							if (compatibleIDsEntry is string[])
							{
								var compatibleIDs = (string[])compatibleIDsEntry;

								foreach (var compatibleID in compatibleIDs)
								{
									if (compatibleID.Equals(hardwareID, StringComparison.InvariantCultureIgnoreCase))
									{
										deviceID = RegistryKeyUtils.GetShortKeyName(deviceKey.Name);
										deviceInstanceID = RegistryKeyUtils.GetShortKeyName(instanceKey.Name);
										// Irrelevant Note: if a device is present but not installed in Windows then ConfigFlags entry will not be present
										// and it doesn't matter anyway because we don't care about how existing installation configure the device

										// there are two reasons not to use DeviceDesc from the local machine:
										// 1. on Windows 6.0+ (or just Windows PE?) the format is different and not compatible with Windows 5.x
										// 2. If the hadrware is present but not installed, the DeviceDesc will be a generic description (e.g. 'Ethernet Controller')

										Console.WriteLine("Found matching device: '" + deviceID + "'");
										return deviceInstanceID;
									}
								}
							}
						}
					}
				}
			}

			return deviceInstanceID;
		}

		private static List<string> GetExportedHardwareCompatibleIDs(string path)
		{
			var result = new List<string>();
			const string keyName = @"SYSTEM\CurrentControlSet\Enum";
			var hiveKey = new ExportedRegistryINI(path).LocalMachine.OpenSubKey(keyName);

			foreach (var enumerator in hiveKey.GetSubKeyNames())
			{
				var enumeratorKey = hiveKey.OpenSubKey(enumerator);
				if (enumeratorKey != null)
				{
					foreach (var deviceKeyName in enumeratorKey.GetSubKeyNames())
					{
						var deviceKey = enumeratorKey.OpenSubKey(deviceKeyName);
						if (deviceKey != null)
						{
							foreach (var instanceKeyName in deviceKey.GetSubKeyNames())
							{
								var instanceKey = deviceKey.OpenSubKey(instanceKeyName);
								if (instanceKey != null)
								{
									var hardwareIDEntry = instanceKey.GetValue("HardwareID", new string[0]);
									if (hardwareIDEntry is string[])
										result.AddRange((string[]) hardwareIDEntry);

									var compatibleIDsEntry = instanceKey.GetValue("CompatibleIDs", new string[0]);
									if (compatibleIDsEntry is string[])
									{
										var compatibleIDs = (string[])compatibleIDsEntry;
										result.AddRange(compatibleIDs);
									}
								}
							}
						}
					}
				}
			}

			return result;
		}
	}
}
