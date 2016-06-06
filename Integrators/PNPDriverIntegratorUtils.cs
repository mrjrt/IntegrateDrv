using System;
using System.Collections.Generic;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.DeviceService;
using IntegrateDrv.HardwareDetectors;
using IntegrateDrv.PNPDriver;
using IntegrateDrv.Utilities.Conversion;
using IntegrateDrv.Utilities.Strings;
using IntegrateDrv.WindowsDirectory;

namespace IntegrateDrv.Integrators
{
	public static class PNPDriverIntegratorUtils
	{
		private static string UISelectHardwareID(PNPDriverDirectory pnpDriverDirectory, WindowsInstallation installation, bool useLocalHardwareConfig, string enumExportPath)
		{
			string hardwareID;
			var containsRootDevices = pnpDriverDirectory.ContainsRootDevices(installation.ArchitectureIdentifier, installation.MinorOSVersion, installation.ProductType);
			// We should not use our detection mechanism if a driver directory contains a root device.
			if (!containsRootDevices && (useLocalHardwareConfig || enumExportPath != string.Empty))
			{
				var matchingHardwareIDs = useLocalHardwareConfig
					? PNPLocalHardwareDetector.DetectMatchingLocalHardware(pnpDriverDirectory, installation.ArchitectureIdentifier,
						installation.MinorOSVersion, installation.ProductType)
					: PNPExportedHardwareDetector.DetectMatchingExportedHardware(enumExportPath, pnpDriverDirectory,
						installation.ArchitectureIdentifier, installation.MinorOSVersion, installation.ProductType);

				var devices = pnpDriverDirectory.ListDevices(installation.ArchitectureIdentifier, installation.MinorOSVersion,
					installation.ProductType);
				// We now have a list of hardware IDs that matches (some of) our devices, let's found out which of the devices match
				var matchingDevices = new List<KeyValuePair<string, string>>();
				foreach (var device in devices)
				{
					if (matchingHardwareIDs.Contains(device.Key))
						matchingDevices.Add(device);
				}
				Console.WriteLine();
				Console.WriteLine("Looking for matching device drivers in directory '{0}':", pnpDriverDirectory.Path);
				hardwareID = UISelectMatchingHardwareID(matchingDevices);
			}
			else
				hardwareID = UISelectMatchingHardwareID(pnpDriverDirectory, installation.ArchitectureIdentifier,
					installation.MinorOSVersion, installation.ProductType);

			return hardwareID;
		}

		private static string UISelectMatchingHardwareID(PNPDriverDirectory driverDirectory, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var devices = driverDirectory.ListDevices(architectureIdentifier, minorOSVersion, productType);
			Console.WriteLine();
			Console.WriteLine("Looking for matching device drivers in directory '{0}':", driverDirectory.Path);
			return UISelectMatchingHardwareID(devices);
		}

		private static string UISelectMatchingHardwareID(List<KeyValuePair<string, string>> devices)
		{
			string hardwareID;

			if (devices.Count > 1)
			{
				Console.WriteLine("Found matching device drivers for the following devices:");
				for (var index = 0; index < devices.Count; index++)
				{
					var oneBasedIndex = index + 1;
					Console.WriteLine("{0}. {1}", oneBasedIndex.ToString("00"), devices[index].Value);
					Console.WriteLine("	Hardware ID: " + devices[index].Key);
				}
				Console.Write("Select the device driver you wish to integrate: ");
				// driver number could be double-digit, so we use ReadLine()
				var selection = Conversion.ToInt32(Console.ReadLine()) - 1;
				if (selection >= 0 && selection < devices.Count)
				{
					hardwareID = devices[selection].Key;
					return hardwareID;
				}

				Console.WriteLine("Error: No device has been selected, exiting.");
				return string.Empty;
			}

			if (devices.Count == 1)
			{
				hardwareID = devices[0].Key;
				var deviceName = devices[0].Value;
				Console.WriteLine("Found one matching device driver:");
				Console.WriteLine("1. " + deviceName);
				return hardwareID;
			}

			Console.WriteLine("No matching device drivers have been found.");
			return string.Empty;
		}

		public static List<BaseDeviceService> IntegratePNPDrivers(List<PNPDriverDirectory> pnpDriverDirectories, WindowsInstallation installation, bool useLocalHardwareConfig, string enumExportPath, bool preconfigure)
		{
			var deviceServices = new List<BaseDeviceService>();
			foreach (var pnpDriverDirectory in pnpDriverDirectories)
			{
				var hardwareID = UISelectHardwareID(pnpDriverDirectory, installation, useLocalHardwareConfig, enumExportPath);
				
				if (hardwareID == string.Empty)
				{
					// No device has been selected, exit.
					// UISelectDeviceID has already printed an error message
					Program.Exit();
				}

				Console.WriteLine("Integrating PNP driver for '" + hardwareID + "'");
				var integrator = new PNPDriverIntegrator(pnpDriverDirectory, installation, hardwareID, useLocalHardwareConfig, enumExportPath, preconfigure);
				integrator.IntegrateDriver();
				deviceServices.AddRange(integrator.DeviceServices);
			}
			return deviceServices;
		}

		/// <param name="architectureIdentifier"></param>
		/// <param name="subdir">Presumably in the following form: '\x86'</param>
		/// <param name="pnpDriverInf"></param>
		/// <param name="sourceFileName"></param>
		private static string GeSourceFileDiskID(INIFile pnpDriverInf, string sourceFileName, string architectureIdentifier, out string subdir)
		{
			// During installation, SetupAPI functions look for architecture-specific SourceDisksFiles sections before using the generic section
			var platformSpecificSectionName = "SourceDisksFiles." + architectureIdentifier;
			var values = pnpDriverInf.GetValuesOfKeyInSection(platformSpecificSectionName, sourceFileName);
			if (values.Count == 0)
				values = pnpDriverInf.GetValuesOfKeyInSection("SourceDisksFiles", sourceFileName);
			// filename=diskid[,[ subdir][,size]]
			var diskID = INIFile.TryGetValue(values, 0);
			subdir = INIFile.TryGetValue(values, 1);
			return diskID;
		}

		/// <returns>
		/// Null if the diskID entry was not found,
		/// otherwise, the path is supposed to be in the following form: '\WinNT'
		/// </returns>
		private static string GeSourceDiskPath(INIFile pnpDriverInf, string diskID, string architectureIdentifier)
		{
			var values = pnpDriverInf.GetValuesOfKeyInSection("SourceDisksNames." + architectureIdentifier, diskID);
			if (values.Count == 0)
				values = pnpDriverInf.GetValuesOfKeyInSection("SourceDisksNames", diskID);

			if (values.Count > 0)
			{
				// diskid = disk-description[,[tag-or-cab-file],[unused],[path],[flags][,tag-file]]
				var path = INIFile.TryGetValue(values, 3);
				// Quoted path is allowed (example: SiS 900-Based PCI Fast Ethernet Adapter driver, version 2.0.1039.1190)
				return QuotedStringUtils.Unquote(path);
			}

			return null;
		}

		// http://msdn.microsoft.com/en-us/library/ff547478%28v=vs.85%29.aspx
		/// <returns>In the following form: 'WinNT\x86\'</returns>
		public static string GetRelativeDirectoryPath(PNPDriverINFFile pnpDriverInf, string sourceFileName, string architectureIdentifier)
		{
			string subdir;
			var diskID = GeSourceFileDiskID(pnpDriverInf, sourceFileName, architectureIdentifier, out subdir);

			if (diskID == string.Empty)
			{
				// file location can come from either [SourceDisksFiles] section for vendor-provided drivers
				// or from layout.inf for Microsoft-provided drivers.
				// if there is no [SourceDisksFiles] section, we assume the user used a Microsoft-provided driver
				// and put all the necessary files in the root driver directory (where the .inf is located)
				//
				// Note: if [SourceDisksFiles] is not present, Windows GUI-mode setup will look for the files in the root driver directory as well.
				return string.Empty;
			}

			if (subdir.StartsWith(@"\"))
				subdir = subdir.Substring(1);

			var relativePathToDisk = GeSourceDiskPath(pnpDriverInf, diskID, architectureIdentifier);
			if (relativePathToDisk == null)
			{
				Console.WriteLine("Warning: Could not locate DiskID '{0}'", diskID);
				return string.Empty; // Which means that the file is in the driver directory
			}
			if (relativePathToDisk == string.Empty)
				// No path, which means that the disk root is the driver directory
				return subdir + @"\";
			if (relativePathToDisk.StartsWith(@"\"))
				// We remove the leading backslash, and return the relative directory name + subdir
				return relativePathToDisk.Substring(1) + @"\" + subdir + @"\";
			Console.WriteLine("Warning: Invalid entry for DiskID '{0}'", diskID);
			return subdir + @"\";
		}

		public static string GetEnumeratorNameFromHardwareID(string hardwareID)
		{
			if (hardwareID.StartsWith("*"))
				return "*";
			var index = hardwareID.IndexOf(@"\", StringComparison.Ordinal);
			var enumerator = hardwareID.Substring(0, index);
			return enumerator;
		}
	}
}
