using System;
using System.Collections.Generic;
using System.Globalization;
using IntegrateDrv.DeviceService;
using IntegrateDrv.Integrators;
using IntegrateDrv.PNPDriver;
using IntegrateDrv.Utilities.FileSystem;
using IntegrateDrv.Utilities.Strings;
using Microsoft.Win32;

namespace IntegrateDrv.BaseClasses
{
	public abstract class PNPDriverIntegratorBase
	{
		private readonly PNPDriverDirectory _driverDirectory;
		private readonly string _architectureIdentifier;
		private readonly int _minorOSVersion;
		private readonly int _productType;
		private readonly string _hardwareID = string.Empty; // enumerator-specific-device-id, e.g. PCI\VEN_8086&DEV_100F

		//private string m_classInstanceID = string.Empty;

		private readonly List<BaseDeviceService> _deviceServices = new List<BaseDeviceService>();
		private readonly List<FileToCopy> _driverFilesToCopy = new List<FileToCopy>();

		protected PNPDriverIntegratorBase(PNPDriverDirectory driverDirectory, string architectureIdentifier,
			int minorOSVersion, int productType, string hardwareID)
		{
			_driverDirectory = driverDirectory;
			_architectureIdentifier = architectureIdentifier;
			_minorOSVersion = minorOSVersion;
			_productType = productType;
			_hardwareID = hardwareID;
		}

		// DDInstall Section in a Network INF File:
		// http://msdn.microsoft.com/en-us/library/ff546329%28VS.85%29.aspx
		protected void ProcessInstallSection(PNPDriverINFFile pnpDriverInf, string installSectionName, string classInstanceID)
		{
			var installSection = pnpDriverInf.GetInstallSection(installSectionName, _architectureIdentifier, _minorOSVersion);

			var softwareKeyName = @"Control\Class\" + pnpDriverInf.ClassGUID + @"\" + classInstanceID;
			foreach (var line in installSection)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				switch (keyAndValues.Key)
				{
					case "AddReg":
					{
						foreach (var registrySectionName in keyAndValues.Value)
							ProcessAddRegSection(pnpDriverInf, registrySectionName, softwareKeyName);
						break;
					}
					case "CopyFiles":
					{
						if (keyAndValues.Value[0].StartsWith("@"))
							ProcessCopyFileDirective(pnpDriverInf, keyAndValues.Value[0].Substring(1));
						else
						{
							foreach (var copyFilesSectionName in keyAndValues.Value)
								ProcessCopyFilesSection(pnpDriverInf, copyFilesSectionName);
						}
						break;
					}
					case "BusType":
					{
						if (pnpDriverInf.IsNetworkAdapter)
						{
							// Some NICs (AMD PCNet) won't start if BusType is not set (CM_PROB_FAILED_START)
							var busType = Convert.ToInt32(keyAndValues.Value[0]);

							SetCurrentControlSetRegistryKey(softwareKeyName, "BusType", RegistryValueKind.String, busType.ToString(CultureInfo.InvariantCulture));
						}
						break;
					}
					case "Characteristics":
					{
						if (pnpDriverInf.IsNetworkAdapter)
						{
							// No evidence so far that the presence of this value is critical, but it's a good practice to add it
							var characteristics = PNPDriverINFFile.ConvertFromIntStringOrHexString(keyAndValues.Value[0]);

							SetCurrentControlSetRegistryKey(softwareKeyName, "Characteristics", RegistryValueKind.DWord, characteristics);
						}
						break;
					}
				}
			}
		}

		protected void ProcessInstallServicesSection(PNPDriverINFFile pnpDriverInf, string installSectionName)
		{
			var installServicesSection = pnpDriverInf.GetInstallServicesSection(installSectionName, _architectureIdentifier,
				_minorOSVersion);

			foreach (var line in installServicesSection)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				switch (keyAndValues.Key)
				{
					case "AddService":
					{
						var serviceName = keyAndValues.Value[0];
						var serviceInstallSection = keyAndValues.Value[2];
						var eventLogInstallSection = INIFile.TryGetValue(keyAndValues.Value, 3);
						var eventLogType = INIFile.TryGetValue(keyAndValues.Value, 4);
						var eventName = INIFile.TryGetValue(keyAndValues.Value, 5);
						ProcessServiceInstallSection(pnpDriverInf, serviceInstallSection, serviceName);
						if (eventLogInstallSection != string.Empty)
						{
							// http://msdn.microsoft.com/en-us/library/ff546326%28v=vs.85%29.aspx
							if (eventLogType == string.Empty)
								eventLogType = "System";
							if (eventName == string.Empty)
								eventName = serviceName;
							ProcessEventLogInstallSection(pnpDriverInf, eventLogInstallSection, eventLogType, eventName);
						}
						break;
					}
				}
			}
		}

		private void ProcessEventLogInstallSection(PNPDriverINFFile pnpDriverInf, string sectionName, string eventLogType,
			string eventName)
		{
			var installSection = pnpDriverInf.GetSection(sectionName);

			var relativeRoot = @"Services\EventLog\" + eventLogType + @"\" + eventName;
			foreach (var line in installSection)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				switch (keyAndValues.Key)
				{
					case "AddReg":
					{
						foreach (var registrySectionName in keyAndValues.Value)
							ProcessAddRegSection(pnpDriverInf, registrySectionName, relativeRoot);
						break;
					}
				}
			}
		}

		/// <param name="sectionName"></param>
		/// <param name="relativeRoot">
		/// The location where HKR entried will be stored, relative to 'SYSTEM\CurrentControlSet\' (or ControlSet001 for that matter)
		/// </param>
		/// <param name="pnpDriverInf"></param>
		private void ProcessAddRegSection(PNPDriverINFFile pnpDriverInf, string sectionName, string relativeRoot)
		{
			var section = pnpDriverInf.GetSection(sectionName);
			foreach (var line in section)
			{
				var values = INIFile.GetCommaSeparatedValues(line);
				var hiveName = values[0];
				var subKeyName = INIFile.Unquote(values[1]);
				var valueName = INIFile.TryGetValue(values, 2);
				var valueType = INIFile.TryGetValue(values, 3);

				var valueDataUnparsed = string.Empty;
				if (values.Count > 3)
					valueDataUnparsed = StringUtils.Join(values.GetRange(4, values.Count - 4), ",");
				// byte-list is separated using commmas

				valueName = INIFile.Unquote(valueName);
				valueType = pnpDriverInf.ExpandToken(valueType);
				var valueTypeFlags = PNPDriverINFFile.ConvertFromIntStringOrHexString(valueType);
				var valueKind = PNPDriverINFFile.GetRegistryValueKind(valueTypeFlags);
				if (valueKind == RegistryValueKind.String)
					valueDataUnparsed = pnpDriverInf.ExpandToken(valueDataUnparsed);
				var valueData = HiveINIFile.ParseValueDataString(valueDataUnparsed, valueKind);

				if (hiveName == "HKR")
				{
					var cssKeyName = relativeRoot;
					if (subKeyName != string.Empty)
						cssKeyName = cssKeyName + @"\" + subKeyName;
					// Note that software key will stick from text-mode:
					SetCurrentControlSetRegistryKey(cssKeyName, valueName, valueKind, valueData);
				}
				else if (hiveName == "HKLM" &&
				         subKeyName.StartsWith(@"SYSTEM\CurrentControlSet\", StringComparison.InvariantCultureIgnoreCase))
				{
					var cssKeyName = subKeyName.Substring(@"SYSTEM\CurrentControlSet\".Length);

					SetCurrentControlSetRegistryKey(cssKeyName, valueName, valueKind, valueData);
				}
			}
		}

		private void ProcessCopyFilesSection(PNPDriverINFFile pnpDriverInf, string sectionName)
		{
			var section = pnpDriverInf.GetSection(sectionName);
			foreach (var line in section)
			{
				var values = INIFile.GetCommaSeparatedValues(line);
				var destinationFileName = values[0];
				var sourceFileName = INIFile.TryGetValue(values, 1);
				if (sourceFileName == string.Empty)
					sourceFileName = destinationFileName;
				ProcessCopyFileDirective(pnpDriverInf, sourceFileName, destinationFileName);
			}
		}

		private void ProcessCopyFileDirective(PNPDriverINFFile pnpDriverInf, string sourceFileName)
		{
			ProcessCopyFileDirective(pnpDriverInf, sourceFileName, sourceFileName);
		}

		private void ProcessCopyFileDirective(PNPDriverINFFile pnpDriverInf, string sourceFileName, string destinationFileName)
		{
			var relativeSourcePath = PNPDriverIntegratorUtils.GetRelativeDirectoryPath(pnpDriverInf, sourceFileName,
				_architectureIdentifier);
			var fileToCopy = new FileToCopy(relativeSourcePath, sourceFileName, destinationFileName);
			if (!FileSystemUtils.IsFileExist(_driverDirectory.Path + fileToCopy.RelativeSourceFilePath))
			{
				Console.WriteLine("Error: Missing file: " + _driverDirectory.Path + fileToCopy.RelativeSourceFilePath);
				Program.Exit();
			}
			// actual copy will be performed later
			_driverFilesToCopy.Add(fileToCopy);
		}

		private void ProcessServiceInstallSection(PNPDriverINFFile pnpDriverInf, string sectionName, string serviceName)
		{
			Console.WriteLine("Registering service '" + serviceName + "'");
			var serviceInstallSection = pnpDriverInf.GetSection(sectionName);

			var displayName = string.Empty;
			var serviceBinary = string.Empty;
			var serviceTypeString = string.Empty;
			var errorControlString = string.Empty;
			var loadOrderGroup = string.Empty;

			//string guiModeRelativeRoot = @"Services\" + serviceName;
			foreach (var line in serviceInstallSection)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				switch (keyAndValues.Key)
				{
					case "AddReg":
					{
						// http://msdn.microsoft.com/en-us/library/ff546326%28v=vs.85%29.aspx
						// AddReg will always come after ServiceBinaryServiceBinary

						var relativeRoot = @"Services\" + serviceName;

						foreach (var registrySectionName in keyAndValues.Value)
							ProcessAddRegSection(pnpDriverInf, registrySectionName, relativeRoot);
						break;
					}
					case "DisplayName":
					{
						displayName = INIFile.TryGetValue(keyAndValues.Value, 0);
						break;
					}
					case "ServiceBinary":
					{
						serviceBinary = INIFile.TryGetValue(keyAndValues.Value, 0);
						break;
					}
					case "ServiceType":
					{
						serviceTypeString = INIFile.TryGetValue(keyAndValues.Value, 0);
						break;
					}
					case "ErrorControl":
					{
						errorControlString = INIFile.TryGetValue(keyAndValues.Value, 0);
						break;
					}
					case "LoadOrderGroup":
					{
						loadOrderGroup = INIFile.TryGetValue(keyAndValues.Value, 0);
						break;
					}
				}
			}

			displayName = pnpDriverInf.ExpandToken(displayName);
			displayName = INIFile.Unquote(displayName);

			var fileName = serviceBinary.Replace(@"%12%\", string.Empty);
			var imagePath = PNPDriverINFFile.ExpandDirID(serviceBinary);

			var serviceType = PNPDriverINFFile.ConvertFromIntStringOrHexString(serviceTypeString);
			var errorControl = PNPDriverINFFile.ConvertFromIntStringOrHexString(errorControlString);

			var deviceDescription = pnpDriverInf.GetDeviceDescription(_hardwareID, _architectureIdentifier, _minorOSVersion,
				_productType);

			BaseDeviceService deviceService;

			if (pnpDriverInf.IsNetworkAdapter)
			{
				// this is a nic, we are binding TCP/IP to it
				// we need a unique NetCfgInstanceID that will be used with Tcpip service and the nic's class
				var netCfgInstanceID = "{" + Guid.NewGuid().ToString().ToUpper() + "}";
				deviceService = new NetworkDeviceService(deviceDescription, serviceName, displayName, loadOrderGroup, serviceType,
					errorControl, fileName, imagePath, netCfgInstanceID);
				_deviceServices.Add(deviceService);
			}
			else
			{
				deviceService = new BaseDeviceService(deviceDescription, serviceName, displayName, loadOrderGroup, serviceType,
					errorControl, fileName, imagePath);
				_deviceServices.Add(deviceService);
			}
		}

		protected void ProcessCoInstallersSection(PNPDriverINFFile pnpDriverInf, string installSectionName)
		{
			var matchingInstallSectionName = pnpDriverInf.GetMatchingInstallSectionName(installSectionName,
				_architectureIdentifier, _minorOSVersion);
			if (matchingInstallSectionName == string.Empty)
				return;
			var matchingCoInstallersSectionName = matchingInstallSectionName + ".CoInstallers";
			var coinstallersSection = pnpDriverInf.GetSection(matchingCoInstallersSectionName);

			foreach (var line in coinstallersSection)
			{
				var keyAndValues = INIFile.GetKeyAndValues(line);
				switch (keyAndValues.Key)
				{
					case "CopyFiles":
					{
						if (keyAndValues.Value[0].StartsWith("@"))
							ProcessCopyFileDirective(pnpDriverInf, keyAndValues.Value[0].Substring(1));
						else
							foreach (var copyFilesSectionName in keyAndValues.Value)
								ProcessCopyFilesSection(pnpDriverInf, copyFilesSectionName);
						break;
					}
				}
			}
		}

		protected abstract void SetCurrentControlSetRegistryKey(string keyName, string valueName, RegistryValueKind valueKind,
			object valueData);

		protected string HardwareID
		{
			get { return _hardwareID; }
		}

		public List<BaseDeviceService> DeviceServices
		{
			get { return _deviceServices; }
		}

		protected List<NetworkDeviceService> NetworkDeviceServices
		{
			get { return DeviceServiceUtils.FilterNetworkDeviceServices(_deviceServices); }
		}

		protected PNPDriverDirectory DriverDirectory
		{
			get { return _driverDirectory; }
		}

		protected List<FileToCopy> DriverFilesToCopy
		{
			get { return _driverFilesToCopy; }
		}
	}

	public class FileToCopy
	{
		private readonly string _relativeSourceDirectory;
		private readonly string _sourceFileName;
		public readonly string DestinationFileName;

		public FileToCopy(string relativeSourceDirectory, string sourceFileName, string destinationFileName)
		{
			_relativeSourceDirectory = relativeSourceDirectory;
			_sourceFileName = sourceFileName;
			DestinationFileName = destinationFileName;
		}

		public string RelativeSourceFilePath
		{
			get { return _relativeSourceDirectory + _sourceFileName; }
		}
	}
}
