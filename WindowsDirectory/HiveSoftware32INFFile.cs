namespace IntegrateDrv.WindowsDirectory
{
	public class HiveSoftware32INFFile : HiveSoftwareINFFile
	{
		public HiveSoftware32INFFile() : base("hivsft32.inf")
		{
		}

		public override void RegisterDriverDirectory(string driverDirectoryWinnt)
		{
			const string subKeyName = "SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion";
			var directory = string.Format("%SystemRoot%\\{0}", driverDirectoryWinnt);
			IncludeDirectoryInDevicePath(subKeyName, directory);
		}
	}
}
