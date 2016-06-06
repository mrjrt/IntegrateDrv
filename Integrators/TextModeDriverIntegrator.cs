using System;
using System.Collections.Generic;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.TextModeDriver;
using IntegrateDrv.Utilities.Conversion;
using IntegrateDrv.WindowsDirectory;

namespace IntegrateDrv.Integrators
{
	public class TextModeDriverIntegrator
	{
		readonly TextModeDriverDirectory _driverDirectory;
		readonly WindowsInstallation _installation;
		readonly string _deviceID = string.Empty;

		public TextModeDriverIntegrator(TextModeDriverDirectory driverDirectory, WindowsInstallation installation, string deviceID)
		{
			_driverDirectory = driverDirectory;
			_installation = installation;
			_deviceID = deviceID;
		}

		public static string UISelectDeviceID(TextModeDriverDirectory driverDirectory)
		{
			var driverINI = driverDirectory.TextModeDriverSetupINI;
			string deviceID;

			if (driverINI.Devices.Count > 1)
			{
				Console.WriteLine("The directory specified contains drivers for the following devices:");
				for (var index = 0; index < driverINI.Devices.Count; index++)
					Console.WriteLine("{0}. {1}", index + 1, driverINI.Devices[index].Value);
				Console.Write("Select the device driver you wish to integrate: ");
				// driver number could be double-digit, so we use ReadLine()
				var selection = Conversion.ToInt32(Console.ReadLine()) - 1;
				if (selection >= 0 && selection < driverINI.Devices.Count)
				{
					deviceID = driverINI.Devices[selection].Key;
					return deviceID;
				}

				Console.WriteLine("Error: No driver has been selected, exiting.");
				return string.Empty;
			}

			if (driverINI.Devices.Count == 1)
			{
				deviceID = driverINI.Devices[0].Key;
				var deviceName = driverINI.Devices[0].Value;
				Console.WriteLine("Found one driver:");
				Console.WriteLine("1. " + deviceName);
				return deviceID;
			}

			Console.WriteLine("Mass storage device driver has not been found.");
			return string.Empty;
		}

		public void IntegrateDriver()
		{
			UpdateTextSetupInformationFileAndCopyFiles(_deviceID);
			var destinationWinntDirectory = _installation.GetDriverDestinationWinntDirectory(_deviceID);

			// hivesft.inf
			_installation.HiveSoftwareInf.RegisterDriverDirectory(destinationWinntDirectory);

			if (_installation.Is64Bit)
				// hivsft32.inf
				_installation.HiveSoftware32Inf.RegisterDriverDirectory(destinationWinntDirectory);
		}

		// update txtsetup.sif and dotnet.inf
		private void UpdateTextSetupInformationFileAndCopyFiles(string deviceID)
		{
			// Files.HwComponent.ID Section
			var driverINI = _driverDirectory.TextModeDriverSetupINI;
			var section = driverINI.GetDriverFilesSection(deviceID);

			var serviceName = string.Empty;
			var driverKeys = new List<string>();

			var sourceDirectoryInMediaRootForm = _installation.GetSourceDriverDirectoryInMediaRootForm(deviceID);
			var sourceDiskID = _installation.TextSetupInf.AllocateSourceDiskID(_installation.ArchitectureIdentifier, sourceDirectoryInMediaRootForm);
			
			var destinationWinntDirectory = _installation.GetDriverDestinationWinntDirectory(_deviceID);
			var destinationWinntDirectoryID = _installation.TextSetupInf.AllocateWinntDirectoryID(destinationWinntDirectory);

			foreach (var line in section)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				var directory = driverINI.GetDirectoryOfDisk(keyAndValues.Value[0]);
				var fileName = keyAndValues.Value[1];
				var sourceFilePath = _driverDirectory.Path + "." + directory + @"\" + fileName;
				var isDriver = keyAndValues.Key.Equals("driver", StringComparison.InvariantCultureIgnoreCase);
				_installation.CopyFileToSetupDriverDirectory(sourceFilePath, deviceID + @"\", fileName);
				
				if (isDriver)
				{
					_installation.CopyDriverToSetupRootDirectory(sourceFilePath, fileName);
					if (_installation.IsTargetContainsTemporaryInstallation)
						_installation.CopyFileFromSetupDirectoryToBootDirectory(fileName);
				}
				
				_installation.TextSetupInf.SetSourceDisksFileEntry(_installation.ArchitectureIdentifier, sourceDiskID, destinationWinntDirectoryID, fileName, FileCopyDisposition.AlwaysCopy);

				if (isDriver)
				{
					// http://msdn.microsoft.com/en-us/library/ff544919%28v=VS.85%29.aspx
					// unlike what one may understand from the reading specs, this value is *only* used to form [Config.DriverKey] section name,
					// and definitely NOT to determine the service subkey name under CurrentControlSet\Services. (which is determined by the service file name without a .sys extension)
					var driverKey = keyAndValues.Value[2];

					// http://support.microsoft.com/kb/885756
					// according to this, only the first driver entry should be processed.

					// http://app.nidc.kr/dirver/IBM_ServerGuide_v7.4.17/sguide/w3x64drv/$oem$/$1/drv/dds/txtsetup.oem
					// however, this sample and my experience suggest that files / registry entries from a second driver entry will be copied / registered,
					// (both under the same Services\serviceName key), so we'll immitate that.
					driverKeys.Add(driverKey);
					
					if (serviceName == string.Empty)
					{
						// Some txtsetup.oem drivers are without HardwareID entries,
						// but we already know that the service is specified by the file name of its executable image without a .sys extension,
						// so we should use that.
						serviceName = TextSetupINFFile.GetServiceName(fileName);
					}
					// We should use FileCopyDisposition.DoNotCopy, because InstructToLoadSCSIDriver will already copy the device driver.
					_installation.TextSetupInf.SetSourceDisksFileDriverEntry(_installation.ArchitectureIdentifier, fileName, FileCopyDisposition.DoNotCopy);
					_installation.TextSetupInf.SetFileFlagsEntryForDriver(fileName);
					var deviceName = driverINI.GetDeviceName(deviceID);
					_installation.TextSetupInf.InstructToLoadSCSIDriver(fileName, deviceName);
				}

				// add file to the list of files to be copied to local source directory
				if (!_installation.IsTargetContainsTemporaryInstallation)
				{
					_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToLocalSourceDriverDirectory(sourceDirectoryInMediaRootForm, fileName);
					if (isDriver)
						_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory(fileName);
				}
			}

			section = driverINI.GetHardwareIdsSection(deviceID);
			foreach (var line in section)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				var hardwareID = keyAndValues.Value[0];
				// http://msdn.microsoft.com/en-us/library/ff546129%28v=VS.85%29.aspx
				// The service is specified by the file name of its executable image without a .sys extension
				// it is incomprehensible that this line will change the value of serviceName, because we already set serviceName to the service file name without a .sys extension
				serviceName = INIFile.Unquote(keyAndValues.Value[1]); 
				hardwareID = INIFile.Unquote(hardwareID);
				_installation.TextSetupInf.AddDeviceToCriticalDeviceDatabase(hardwareID, serviceName);
			}

			foreach(var driverKey in driverKeys)
			{
				section = driverINI.GetConfigSection(driverKey);
				foreach (var line in section)
				{
					var keyAndValues = INIFile.GetKeyAndValues(line);
					var subKeyNameQuoted = keyAndValues.Value[0];
					var valueName = keyAndValues.Value[1];
					var valueType = keyAndValues.Value[2];
					var valueDataUnparsed = keyAndValues.Value[3];
					var valueKind = TextModeDriverSetupINIFile.GetRegistryValueKind(valueType);
					var valueData = HiveINIFile.ParseValueDataString(valueDataUnparsed, valueKind);
					var subKeyName = INIFile.Unquote(subKeyNameQuoted);

					_installation.HiveSystemInf.SetServiceRegistryKey(serviceName, subKeyName, valueName, valueKind, valueData);
					_installation.SetupRegistryHive.SetServiceRegistryKey(serviceName, subKeyName, valueName, valueKind, valueData);
				}
			}
		}

		public static void IntegrateTextModeDrivers(List<TextModeDriverDirectory> textModeDriverDirectories, WindowsInstallation installation)
		{
			foreach (var textModeDriverDirectory in textModeDriverDirectories)
			{
				var deviceID = UISelectDeviceID(textModeDriverDirectory);
				if (deviceID == string.Empty)
					// No device has been selected, exit.
					// UISelectDeviceID has already printed an error message
					Program.Exit();

				var integrator = new TextModeDriverIntegrator(textModeDriverDirectory, installation, deviceID);
				integrator.IntegrateDriver();
			}
		}
	}
}
