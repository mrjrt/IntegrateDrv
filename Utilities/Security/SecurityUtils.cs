using System;
using System.Runtime.InteropServices;

namespace IntegrateDrv.Utilities.Security
{
	public enum SecurityPrivilegeName
	{
		SeBackupPrivilege,
		SeManageVolumePrivilege,
		SeRestorePrivilege,
	}

	// ReSharper disable InconsistentNaming
	public static class SecurityUtils
	{
		public const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
		public const int TOKEN_QUERY = 0x00000008;

		public const int SE_PRIVILEGE_ENABLED = 0x00000002;

		public const int ERROR_SUCCESS = 0x00;

		[StructLayout(LayoutKind.Sequential)]
		public struct LUID
		{
			public int LowPart;
			public int HighPart;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct TOKEN_PRIVILEGES
		{
			public int PrivilegeCount;
			public LUID Luid;
			public int Attributes;
		}

		[DllImport("advapi32.dll", CharSet = CharSet.Auto)]
		public static extern int OpenProcessToken(int processHandle, int desiredAccess,
		ref int tokenhandle);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		public static extern int GetCurrentProcess();

		[DllImport("advapi32.dll", CharSet = CharSet.Auto)]
		public static extern int LookupPrivilegeValue(string lpsystemname, string lpname,
		[MarshalAs(UnmanagedType.Struct)] ref LUID lpLuid);

		[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		public static extern int AdjustTokenPrivileges(int tokenhandle, int disableprivs, [MarshalAs(UnmanagedType.Struct)]ref TOKEN_PRIVILEGES newstate,
			int bufferlength, int previousState, int returnLength);

		public static bool ObtainPrivilege(SecurityPrivilegeName privilegeName)
		{
			var tokenHandle = 0;

			var tokenPrivileges = new TOKEN_PRIVILEGES();
			var privilegeLuid = new LUID();

			var retval = OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref tokenHandle);
			if (retval == 0) //If the function succeeds, the return value is nonzero
				return false;

			retval = LookupPrivilegeValue(null, privilegeName.ToString(), ref privilegeLuid);
			if (retval == 0) //If the function succeeds, the return value is nonzero
				return false;

			tokenPrivileges.PrivilegeCount = 1;
			tokenPrivileges.Attributes = SE_PRIVILEGE_ENABLED;
			tokenPrivileges.Luid = privilegeLuid;

			retval = AdjustTokenPrivileges(tokenHandle, 0, ref tokenPrivileges, 0, 0, 0);
			if (retval == 0) // If the function succeeds, the return value is nonzero 
				return false;

			// http://msdn.microsoft.com/en-us/library/windows/desktop/aa375202%28v=vs.85%29.aspx
			// GetLastError returns one of the following values when the function succeeds:
			// ERROR_SUCCESS, ERROR_NOT_ALL_ASSIGNED

			var error = Marshal.GetLastWin32Error();
			return (error == ERROR_SUCCESS);
		}

		public static void ObtainBackupRestorePrivileges()
		{
			var success = ObtainPrivilege(SecurityPrivilegeName.SeBackupPrivilege);
			if (success)
				success = ObtainPrivilege(SecurityPrivilegeName.SeRestorePrivilege);

			if (!success)
			{
				Console.WriteLine("Error: Failed to obtain token privilege.");
				Environment.Exit(-1);
			}
		}
	}
}
