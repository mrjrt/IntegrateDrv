using System;
using System.Collections.Generic;
using System.IO;
using IntegrateDrv.Utilities.FileSystem;

namespace IntegrateDrv.PNPDriver
{
	public class PNPDriverDirectory
	{
		private readonly string _path = string.Empty;
		private readonly List<PNPDriverINFFile> _infList;

		private List<KeyValuePair<string, string>> _devices;
			// this include list of the devices from all the INFs in the directory

		public PNPDriverDirectory(string path)
		{
			_path = path;

			var fileNames = GetINFFileNamesInDirectory(path);
			_infList = new List<PNPDriverINFFile>();
			foreach (var fileName in fileNames)
			{
				var driverInf = new PNPDriverINFFile(fileName);
				driverInf.ReadFromDirectory(path);
				_infList.Add(driverInf);
			}
		}

		public bool ContainsINFFiles
		{
			get { return (_infList.Count > 0); }
		}

		private static IEnumerable<string> GetINFFileNamesInDirectory(string path)
		{
			var result = new List<string>();
			var filePaths = new string[0];
			try
			{
				filePaths = Directory.GetFiles(path, "*.inf");
			}
			catch (DirectoryNotFoundException)
			{
			}
			catch (ArgumentException) // such as "Path contains invalid chars"
			{
			}

			foreach (var filePath in filePaths)
			{
				var fileName = FileSystemUtils.GetNameFromPath(filePath);
				result.Add(fileName);
			}
			return result;
		}

		public string GetDeviceInstallSectionName(string hardwareIDToFind, string architectureIdentifier, int minorOSVersion,
			int productType, out PNPDriverINFFile pnpDriverInf)
		{
			foreach (var driverInf in _infList)
			{
				var installSectionName = driverInf.GetDeviceInstallSectionName(hardwareIDToFind, architectureIdentifier,
					minorOSVersion, productType);
				if (installSectionName != string.Empty)
				{
					pnpDriverInf = driverInf;
					return installSectionName;
				}
			}
			pnpDriverInf = null;
			return string.Empty;
		}

		public List<KeyValuePair<string, string>> ListDevices(string architectureIdentifier, int minorOSVersion,
			int productType)
		{
			if (_devices == null)
			{
				_devices = new List<KeyValuePair<string, string>>();
				foreach (var driverInf in _infList)
					_devices.AddRange(driverInf.ListDevices(architectureIdentifier, minorOSVersion, productType));
			}
			return _devices;
		}

		public bool ContainsRootDevices(string architectureIdentifier, int minorOSVersion, int productType)
		{
			foreach (var driverInf in _infList)
			{
				if (driverInf.ContainsRootDevices(architectureIdentifier, minorOSVersion, productType))
					return true;
			}
			return false;
		}

		public string Path
		{
			get { return _path; }
		}
	}
}