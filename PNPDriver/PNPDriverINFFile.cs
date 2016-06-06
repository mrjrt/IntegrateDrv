using System;
using System.Collections.Generic;
using System.Globalization;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.Utilities.Conversion;
using IntegrateDrv.Utilities.Strings;
using Microsoft.Win32;

namespace IntegrateDrv.PNPDriver
{
	public class PNPDriverINFFile : ServiceINIFile
	{
		public const string NetworkAdapterClassName = "Net";
		public const string NetworkAdapterClassGUID = "{4D36E972-E325-11CE-BFC1-08002BE10318}";

		private string _className;
		private string _classGUID;
		private string _provider;
		private string _catalogFile;
		private string _driverVersion;
		
		List<KeyValuePair<string, string>> _devices;

		public PNPDriverINFFile(string fileName) : base(fileName)
		{
		}

		public void SetServiceToBootStart(string installSectionName, string architectureIdentifier, int minorOSVersion)
		{
			SetServiceToBootStart(installSectionName, architectureIdentifier, minorOSVersion, false);
		}

		private void SetServiceToBootStart(string installSectionName, string architectureIdentifier, int minorOSVersion, bool updateConsole)
		{
			var installServicesSection = GetInstallServicesSection(installSectionName, architectureIdentifier, minorOSVersion);
			foreach (var line in installServicesSection)
			{
				var keyAndValues = GetKeyAndValues(line);
				if (keyAndValues.Key == "AddService")
				{
					var serviceName = keyAndValues.Value[0];
					var serviceInstallSection = keyAndValues.Value[2];
					SetServiceToBootStart(serviceInstallSection);
					if (updateConsole)
						Console.WriteLine("Service '" + serviceName + "' has been set to boot start");
				}
			}
		}

		// str can be either be a token or not
		public string ExpandToken(string str)
		{
			var leftIndex = str.IndexOf('%');
			if (leftIndex == 0)
			{
				var rightIndex = str.IndexOf('%', 1);
				if (rightIndex >= 0 && rightIndex == str.Length - 1)
				{
					var token = str.Substring(leftIndex + 1, rightIndex - leftIndex - 1);
					var tokenValue = GetTokenValue(token);
					return tokenValue;
				}
			}
			return str;
		}

		private string GetTokenValue(string token)
		{
			var strings = GetSection("Strings");
			foreach (var line in strings)
			{
				var keyAndValues = GetKeyAndValues(line);
				if (keyAndValues.Key.Equals(token, StringComparison.InvariantCultureIgnoreCase))
					return keyAndValues.Value[0];
			}
			throw new KeyNotFoundException(string.Format("Inf file '{0}' is not valid, token '{1}' was not found!", FileName, token));
		}

		public static string ExpandDirID(string str)
		{
			var leftIndex = str.IndexOf('%');
			if (leftIndex >= 0)
			{
				var rightIndex = str.IndexOf('%', leftIndex + 1);
				if (rightIndex >= 0)
				{
					var token = str.Substring(leftIndex + 1, rightIndex - leftIndex - 1);
					var tokenValue = GetDirIDValue(token);
					str = str.Substring(0, leftIndex) + tokenValue + str.Substring(rightIndex + 1);
				}
			}
			return str;
		}

		private static string GetDirIDValue(string token)
		{
			if (token == "11")
				return "system32";
			if (token == "12")
				return @"system32\drivers";

			throw new Exception("Inf file is not valid, dir-id not found!");
		}

		private IEnumerable<string> ListManufacturerIDs()
		{
			var manufacturerIDs = new List<string>();

			var manufacturers = GetSection("Manufacturer");
			foreach (var manufacturer in manufacturers)
			{
				var manufacturerKeyAndValues = GetKeyAndValues(manufacturer);
				if (manufacturerKeyAndValues.Value.Count >= 1)
				{
					var manufacturerID = manufacturerKeyAndValues.Value[0];
					manufacturerIDs.Add(manufacturerID);
				}
			}
			return manufacturerIDs;
		}

		public string GetDeviceInstallSectionName(string hardwareIDToFind, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var manufacturerIDs = ListManufacturerIDs();
			foreach (var manufacturerID in manufacturerIDs)
			{
				var models = GetModelsSection(manufacturerID, architectureIdentifier, minorOSVersion, productType);
				foreach (var model in models)
				{
					var modelKeyAndValues = GetKeyAndValues(model);
					if (modelKeyAndValues.Value.Count >= 2)
					{
						var hardwareID = modelKeyAndValues.Value[1];
						if (string.Equals(hardwareID, hardwareIDToFind, StringComparison.InvariantCultureIgnoreCase))
						{ 
							var installSectionName = modelKeyAndValues.Value[0];
							return installSectionName;
						}
					}
				}
			}
			return string.Empty;
		}

		/// <summary>
		/// KeyValuePair contains HardwareID, DeviceName
		/// </summary>
		public IEnumerable<KeyValuePair<string, string>> ListDevices(string architectureIdentifier, int minorOSVersion, int productType)
		{
			if (_devices == null)
			{
				_devices = new List<KeyValuePair<string, string>>();

				var manufacturerIDs = ListManufacturerIDs();
				
				foreach (var manufacturerID in manufacturerIDs)
				{
					var models = GetModelsSection(manufacturerID, architectureIdentifier, minorOSVersion, productType);
					foreach (var model in models)
					{
						var modelKeyAndValues = GetKeyAndValues(model);
						if (modelKeyAndValues.Value.Count >= 2)
						{
							string deviceName;
							try
							{
								// in XP x86 SP3, scsi.inf has a missing token,
								// let's ignore the device in such case
								deviceName = Unquote(ExpandToken(modelKeyAndValues.Key));
							}
							catch (KeyNotFoundException ex)
							{
								Console.WriteLine(ex.Message);
								continue;
							}
							var hardwareID = modelKeyAndValues.Value[1];
							_devices.Add(new KeyValuePair<string, string>(hardwareID, deviceName));
						}
					}
				}
			}
			return _devices;
		}

		public bool ContainsRootDevices(string architectureIdentifier, int minorOSVersion, int productType)
		{
			var devices = ListDevices(architectureIdentifier, minorOSVersion, productType);
			foreach(var device in devices)
			{
				var hardwareID = device.Key;
				if (IsRootDevice(hardwareID))
					return true;
			}
			return false;
		}

		public string GetDeviceManufacturerName(string hardwareIDToFind, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var manufacturers = GetSection("Manufacturer");
			foreach (var manufacturer in manufacturers)
			{
				var manufacturerKeyAndValues = GetKeyAndValues(manufacturer);
				if (manufacturerKeyAndValues.Value.Count >= 1)
				{
					var manufacturerName = Unquote(ExpandToken(manufacturerKeyAndValues.Key));
					var manufacturerID = manufacturerKeyAndValues.Value[0];

					var models = GetModelsSection(manufacturerID, architectureIdentifier, minorOSVersion, productType);
					foreach (var model in models)
					{
						var modelKeyAndValues = GetKeyAndValues(model);
						if (modelKeyAndValues.Value.Count >= 2)
						{
							var hardwareID = modelKeyAndValues.Value[1];
							if (hardwareID.Equals(hardwareIDToFind)) // both hardwareIDs comes from the .inf so they must use the same case
								return manufacturerName;
						}
					}
				}
			}
			return string.Empty;
		}

		public string GetDeviceDescription(string hardwareID, string architectureIdentifier, int minorOSVersion, int productType)
		{
			foreach (var device in ListDevices(architectureIdentifier, minorOSVersion, productType))
			{
				if (device.Key.Equals(hardwareID))
					return device.Value;
			}
			return string.Empty;
		}

		/// <summary>
		/// Sorted by priority
		/// </summary>
		public List<string> GetModelsSectionNames(string manufacturerID, string architectureIdentifier, int minorOSVersion, int productType)
		{
			// INF File Platform Extensions and x86-Based Systems:
			// http://msdn.microsoft.com/en-us/library/ff547425%28v=vs.85%29.aspx
			//
			// INF File Platform Extensions and x64-Based Systems
			// http://msdn.microsoft.com/en-us/library/ff547417%28v=vs.85%29.aspx

			// http://msdn.microsoft.com/en-us/library/ff547454%28v=vs.85%29.aspx
			// If the INF contains INF Models sections for several major or minor operating system version numbers,
			// Windows uses the section with the highest version numbers that are not higher than the operating
			// system version on which the installation is taking place. 

			// http://msdn.microsoft.com/en-us/library/ff539924%28v=vs.85%29.aspx
			// TargetOSVersion decoration format:
			// nt[Architecture][.[OSMajorVersion][.[OSMinorVersion][.[ProductType][.SuiteMask]]]]

			var result = new List<string>();
			// Windows 2000 does not support platform extensions on an INF Models section name
			if (minorOSVersion != 0)
			{
				var minor = minorOSVersion;
				while (minor >= 0)
				{
					var sectionName = string.Format("{0}.nt{1}.5", manufacturerID, architectureIdentifier);
					// Even though Windows 2000 does not support platform extensions on models section name, Windows XP / Server 2003 can still use [xxxx.NTx86.5]
					if (minor != 0)
					{
						sectionName += "." + minor;
						result.Add(sectionName + "." + productType);
					}
					result.Add(sectionName);
					minor--;
				}
				
				result.Add(manufacturerID + ".nt" + architectureIdentifier);
				// Starting from Windows Server 2003 SP1, only x86 bases systems can use the .nt platform extension / no platform extension on the Models section,
				// There is no point in supporting the not-recommended .nt platform extension / no platform extension for non-x86 drivers,
				// because surely an updated driver that uses the recommended platform extension exist (such driver will work for both Pre-SP1 and SP1+)
				if (architectureIdentifier == "x86")
				{
					minor = minorOSVersion;
					while (minor >= 0)
					{
						var sectionName = string.Format("{0}.nt.5", manufacturerID);
						if (minor != 0)
						{
							sectionName += "." + minor;
							result.Add(sectionName + "." + productType);
						}
						result.Add(sectionName);
						minor--;
					}

					result.Add(manufacturerID + ".nt");
				}
			}
			result.Add(manufacturerID);

			return result;
		}

		private string GetMatchingModelsSectionName(string manufacturerID, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var modelsSectionNames = GetModelsSectionNames(manufacturerID, architectureIdentifier, minorOSVersion, productType);

			foreach (var modelsSectionName in modelsSectionNames)
			{
				if (StringUtils.ContainsCaseInsensitive(SectionNames, modelsSectionName))
					return modelsSectionName;
			}

			return string.Empty;
		}

		/// <param name="architectureIdentifier"></param>
		/// <param name="minorOSVersion">We know that the major OS version is 5. XP x64, Server 2003 are 5.2, XP x86 is 5.1, Windows 2000 is 5.0</param>
		/// <param name="manufacturerID"></param>
		/// <param name="productType"></param>
		private IEnumerable<string> GetModelsSection(string manufacturerID, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var modelsSectionName = GetMatchingModelsSectionName(manufacturerID, architectureIdentifier, minorOSVersion, productType);
			return modelsSectionName != string.Empty
				? GetSection(modelsSectionName)
				: new List<string>();
		}

		/// <summary>
		/// Sorted by priority
		/// </summary>
		public List<string> GetInstallSectionNames(string installSectionName, string architectureIdentifier, int minorOSVersion)
		{
			// Make these regex-safe
			installSectionName = installSectionName.Replace(@".", @"\.");
			architectureIdentifier = architectureIdentifier.Replace(@".", @"\.");

			// http://msdn.microsoft.com/en-us/library/ff547344%28v=vs.85%29.aspx
			var result = new List<string>();
			while (minorOSVersion >= 0)
			{
				result.Add(
					string.Format(@"{0}(\..+)?\.nt{1}\.5{2}",
						installSectionName,
						architectureIdentifier,
						minorOSVersion != 0
							? @"\." + minorOSVersion
							: @""
					)
				);
				minorOSVersion--;
			}

			result.Add(string.Format(@"{0}(\..+)?\.nt{1}", installSectionName, architectureIdentifier));
			result.Add(string.Format(@"{0}(\..+)?\.nt", installSectionName));
			result.Add(string.Format(@"{0}", installSectionName));
			return result;
		}

		public string GetMatchingInstallSectionName(string installSectionName, string architectureIdentifier, int minorOSVersion)
		{
			var installSectionNames = GetInstallSectionNames(installSectionName, architectureIdentifier, minorOSVersion);
			foreach (var sectionName in installSectionNames)
			{
				var sectionResult = StringUtils.ContainsRegex(SectionNames, sectionName + @"\.Services", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				if (!string.IsNullOrEmpty(sectionResult))
					return sectionResult;
			}
			return string.Empty;
		}

		public IEnumerable<string> GetInstallSection(string installSectionName, string architectureIdentifier, int minorOSVersion)
		{
			var matchingInstallSectionName = GetMatchingInstallSectionName(installSectionName, architectureIdentifier, minorOSVersion);
			return matchingInstallSectionName != string.Empty
				? GetSection(matchingInstallSectionName)
				: new List<string>();
		}

		public IEnumerable<string> GetInstallServicesSection(string installSectionName, string architectureIdentifier, int minorOSVersion)
		{
			var installSectionNames = GetInstallSectionNames(installSectionName, architectureIdentifier, minorOSVersion);
			foreach (var sectionName in installSectionNames)
			{
				var sectionResult = StringUtils.ContainsRegex(SectionNames, sectionName + @"\.Services", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				if(!string.IsNullOrEmpty(sectionResult))
					return GetSection(sectionResult);
			}

			return new List<string>();
		}

		public bool DisableMatchingHardwareID(string hardwareIDToDisable, string architectureIdentifier, int minorOSVersion, int productType)
		{
			var found = false;
			var genericHardwareID = GetGenericHardwareID(hardwareIDToDisable);

			var manufacturerIDs = ListManufacturerIDs();
			foreach (var manufacturerID in manufacturerIDs)
			{
				var modelsSectionName = GetMatchingModelsSectionName(manufacturerID, architectureIdentifier, minorOSVersion, productType);
				if (modelsSectionName == string.Empty)
					continue;
				var models = GetSection(modelsSectionName);
				
				foreach (var model in models)
				{
					var modelKeyAndValues = GetKeyAndValues(model);
					if (modelKeyAndValues.Value.Count >= 2)
					{
						var hardwareID = modelKeyAndValues.Value[1];

						if (hardwareID.StartsWith(genericHardwareID, StringComparison.InvariantCultureIgnoreCase))
						{
							var lineIndex = GetLineIndex(modelsSectionName, model);
							UpdateLine(lineIndex, ";" + model);
							
							found = true;
						}
					}
				}
			}

			return found;
		}

		public string ClassName
		{
			get
			{
				if (_className == null)
				{
					var values = GetValuesOfKeyInSection("Version", "Class");
					_className = values.Count >= 1
						? values[0]
						: string.Empty;
				}
				return _className;
			}
		}

		public string ClassGUID
		{
			get
			{
				if (_classGUID == null)
				{
					var values = GetValuesOfKeyInSection("Version", "ClassGUID");
					_classGUID = values.Count >= 1
						? values[0].ToUpper()
						: string.Empty;
				}
				return _classGUID;
			}
		}

		public string Provider
		{
			get
			{
				if (_provider == null)
				{
					var values = GetValuesOfKeyInSection("Version", "Provider");
					_provider = values.Count >= 1
						? Unquote(ExpandToken(values[0]))
						: string.Empty;
				}
				return _provider;
			}
		}

		public string CatalogFile
		{
			get
			{
				if (_catalogFile == null)
				{
					var values = GetValuesOfKeyInSection("Version", "CatalogFile");
					_catalogFile = values.Count >= 1
						? values[0]
						: string.Empty;
				}
				return _catalogFile;
			}
		}

		public string DriverVersion
		{
			get
			{
				if (_driverVersion == null)
				{
					var values = GetValuesOfKeyInSection("Version", "DriverVer");
					// DriverVer=mm/dd/yyyy[,w.x.y.z]
					_driverVersion = values.Count >= 2
						? values[1]
						: string.Empty;
				}
				return _driverVersion;
			}
		}

		public static RegistryValueKind GetRegistryValueKind(string hexStringValueTypeflags)
		{
			var flags = ConvertFromIntStringOrHexString(hexStringValueTypeflags);
			return GetRegistryValueKind(flags);
		}

		public static RegistryValueKind GetRegistryValueKind(int flags)
		{
			const int legalValues = 0x00000001 | 0x00010000 | 0x00010001 | 0x00020000;
			var value = flags & legalValues;
			switch (value)
			{
				case 0x00000000:
					return RegistryValueKind.String;
				case 0x00000001:
					return RegistryValueKind.Binary;
				case 0x00010000:
					return RegistryValueKind.MultiString;
				case 0x00010001:
					return RegistryValueKind.DWord;
				case 0x00020000:
					return RegistryValueKind.ExpandString;
				default:
					return RegistryValueKind.Unknown;
			}
		}

		public static int ConvertFromIntStringOrHexString(string value)
		{
			return value.StartsWith("0x")
				? Int32.Parse(value.Substring(2), NumberStyles.AllowHexSpecifier)
				: Conversion.ToInt32(value);
		}

		public bool IsNetworkAdapter
		{
			get
			{
				return (string.Equals(ClassName, NetworkAdapterClassName, StringComparison.InvariantCultureIgnoreCase) ||
						ClassGUID == NetworkAdapterClassGUID);
			}
		}

		// To the best of my limited knowledge all root devices are virtual except ACPI_HAL and such,
		// but there are many virtual devices that are not root devices (e.g. use the node ID of the PC's host controller)
		public static bool IsRootDevice(string hardwareID)
		{
			return hardwareID.ToLower().StartsWith(@"root\");
		}

		/// <summary>
		/// This method will remove the SUBSYS and REV entries from hardwareID
		/// </summary>
		private static string GetGenericHardwareID(string hardwareID)
		{
			var genericHardwareID = hardwareID;
			var subsysIndex = hardwareID.ToUpper().IndexOf("&SUBSYS", StringComparison.Ordinal);
			if (subsysIndex >= 0)
				genericHardwareID = hardwareID.Substring(0, subsysIndex);

			// sometimes &REV appears without &SUBSYS
			var revIndex = hardwareID.ToUpper().IndexOf("&REV", StringComparison.Ordinal);
			if (revIndex >= 0)
				genericHardwareID = hardwareID.Substring(0, revIndex);
			return genericHardwareID;
		}
	}
}
