using System.Runtime.InteropServices;
using IntegrateDrv.Utilities.Security;

namespace IntegrateDrv.Utilities.Registry
{
	public static class RegistryUtils
	{
		// ReSharper disable once InconsistentNaming
		public const uint HKEY_USERS = 0x80000003;

		[DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern int RegLoadKey(uint hKey, string lpSubKey, string lpFile);

		[DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		public static extern int RegUnLoadKey(uint hKey, string lpSubKey);

		public static int LoadHive(string subkey, string hivePath)
		{
			SecurityUtils.ObtainBackupRestorePrivileges();
			
			var result = RegLoadKey(HKEY_USERS, subkey, hivePath);
			return result;
		}

		public static int UnloadHive(string subkey)
		{
			SecurityUtils.ObtainBackupRestorePrivileges();
			return RegUnLoadKey(HKEY_USERS, subkey);
		}
	}
}
