using System.Collections.Generic;
using IntegrateDrv.BaseClasses;
using Microsoft.Win32;

namespace IntegrateDrv.TextModeDriver
{
	// TxtSetup.oem File Format: http://msdn.microsoft.com/en-us/library/ff553509%28v=vs.85%29.aspx
	public class TextModeDriverSetupINIFile : INIFile
	{
		List<KeyValuePair<string, string>> _devices;

		public TextModeDriverSetupINIFile() : base("txtsetup.oem")
		{
		}

		public string GetDirectoryOfDisk(string diskName)
		{
			var section = GetSection("Disks");
			foreach (var line in section)
			{
				var keyAndValues = GetKeyAndValues(line);
				if (keyAndValues.Key == diskName)
				{
					var directory = keyAndValues.Value[2];
					return directory;
				}
			}

			return string.Empty;
		}

		public List<string> GetDriverFilesSection(string deviceID)
		{
			var sectionName = string.Format("Files.scsi.{0}", deviceID);
			return GetSection(sectionName);
		}

		public List<string> GetHardwareIdsSection(string deviceID)
		{
			var sectionName = string.Format("HardwareIds.scsi.{0}", deviceID);
			return GetSection(sectionName);
		}

		public List<string> GetConfigSection(string driverKey)
		{
			var sectionName = string.Format("Config.{0}", driverKey);
			return GetSection(sectionName);
		}

		public string GetDeviceName(string deviceID)
		{
			foreach (var keyAndValue in Devices)
			{
				if (keyAndValue.Key.Equals(deviceID))
					return keyAndValue.Value;
			}
			return string.Empty;
		}

		/// <summary>
		/// KeyValuePair contains Device ID, Device Name
		/// </summary>
		public List<KeyValuePair<string,string>> Devices
		{
			get
			{
				if (_devices == null)
				{
					var section = GetSection("scsi");
					_devices = new List<KeyValuePair<string, string>>();
					foreach (var line in section)
					{
						var keyAndValues = GetKeyAndValues(line);
						if (keyAndValues.Value.Count > 0)
						{
							var deviceName = Unquote(keyAndValues.Value[0]);
							_devices.Add(new KeyValuePair<string, string>(keyAndValues.Key, deviceName));
						}
					}
				}
				return _devices;
			}
		}

		public static RegistryValueKind GetRegistryValueKind(string valueTypeString)
		{
			switch (valueTypeString)
			{
				case "REG_DWORD":
					return RegistryValueKind.DWord;
				case "REG_QWORD":
					return RegistryValueKind.QWord;
				case "REG_BINARY":
					return RegistryValueKind.Binary;
				case "REG_SZ":
					return RegistryValueKind.String;
				case "REG_EXPAND_SZ":
					return RegistryValueKind.ExpandString;
				case "REG_MULTI_SZ":
					return RegistryValueKind.MultiString;
				default:
					return RegistryValueKind.Unknown;
			}
		}
	}
}
