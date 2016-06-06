using IntegrateDrv.BaseClasses;
using IntegrateDrv.Utilities.Strings;

namespace IntegrateDrv.WindowsDirectory
{
	public class HiveSoftwareINFFile : HiveINIFile
	{
		public HiveSoftwareINFFile() : base("hivesft.inf")
		{
		}

		protected HiveSoftwareINFFile(string fileName) : base(fileName)
		{
		}

		/// <summary>
		/// Should return 'Microsoft Windows 2000', 'Microsoft Windows XP' or 'Microsoft Windows Server 2003'
		/// </summary>
		public string GetWindowsProductName()
		{
			const string hive = "HKLM";
			const string subKeyName = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
			const string valueName = "ProductName";
			var productName = GetRegistryValueData(hive, subKeyName, valueName);
			return productName;
		}

		virtual public void RegisterDriverDirectory(string driverDirectoryWinnt)
		{
			const string subKeyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
			var directory = string.Format(@"%SystemRoot%\{0}", driverDirectoryWinnt);
			IncludeDirectoryInDevicePath(subKeyName, directory);
		}

		protected void IncludeDirectoryInDevicePath(string subKeyName, string directory)
		{
			const string hive = "HKLM";
			const string valueName = "DevicePath";
			var path = GetRegistryValueData(hive, subKeyName, valueName);
			var directories = StringUtils.Split(path, ';');
			if (!directories.Contains(directory))
				//directories.Add(directory);
				directories.Insert(0, directory); // added directories should have higher priority than %SystemRoot%\inf
			path = StringUtils.Join(directories,";");
			path = Quote(path);
			UpdateRegistryValueData(hive, subKeyName, valueName, path);
			IsModified = true;
		}
	}
}
