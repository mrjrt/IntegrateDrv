using System;
using System.IO;
using IntegrateDrv.Utilities.FileSystem;

namespace IntegrateDrv.WindowsDirectory
{
	public class WindowsInstallation
	{
		private const string ScsiDriversSourceDirectory = "DRIVERS\\";
			// this is where the files will reside in the local source directory

		private const string ScsiDriversDestinationDirectory = "SYSTEM32\\Driver Cache\\i386\\";
			// this is where the files will reside during windows GUI setup phase

		private readonly string _localSourceDirectory = string.Empty;

		public WindowsInstallation(string targetDirectory)
		{
			BootDirectory = string.Empty;
			TargetDirectory = targetDirectory;
			if (FileSystemUtils.IsDirectoryExist(targetDirectory + "$WIN_NT$.~BT") &&
			    FileSystemUtils.IsDirectoryExist(targetDirectory + "$WIN_NT$.~LS"))
			{
				BootDirectory = targetDirectory + "$WIN_NT$.~BT" + "\\";
				_localSourceDirectory = targetDirectory + "$WIN_NT$.~LS" + "\\";
			}
			else if (FileSystemUtils.IsDirectoryExist(targetDirectory + "I386") ||
			         FileSystemUtils.IsDirectoryExist(targetDirectory + "IA64") ||
			         FileSystemUtils.IsDirectoryExist(targetDirectory + "amd64"))
			{
				_localSourceDirectory = targetDirectory;
			}

			if (IsTargetValid)
				LoadFiles();
		}

		private void LoadFiles()
		{
			TextSetupInf = new TextSetupINFFile();
			TextSetupInf.ReadFromDirectory(SetupDirectory);

			HiveSoftwareInf = new HiveSoftwareINFFile();
			HiveSoftwareInf.ReadFromDirectory(SetupDirectory);

			if (Is64Bit)
			{
				HiveSoftware32Inf = new HiveSoftware32INFFile();
				HiveSoftware32Inf.ReadFromDirectory(SetupDirectory);
			}

			HiveSystemInf = new HiveSystemINFFile();
			HiveSystemInf.ReadFromDirectory(SetupDirectory);

			if (!IsTargetContainsTemporaryInstallation)
			{
				// integration to installation media
				DOSNetInf = new DOSNetINFFile();
				DOSNetInf.ReadFromDirectory(SetupDirectory);
			}

			SetupRegistryHive = new SetupRegistryHiveFile();
			NetGPCInf = new NetGPCINFFile();
			NetGPCInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			NetPacketSchedulerInf = new NetPacketSchedulerINFFile();
			NetPacketSchedulerInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			NetPacketSchedulerAdapterInf = new NetPacketSchedulerAdapterINFFile();
			NetPacketSchedulerAdapterInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			NetTCPIPInf = new NetTCPIPINFFile();
			NetTCPIPInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			HalInf = new HALINFFile();
			HalInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			UsbInf = new UsbINFFile();
			UsbInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			UsbStorageClassDriverInf = new UsbStorageClassDriverINFFile();
			UsbStorageClassDriverInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);

			if (!IsWindows2000) // usbport.inf does not exist in Windows 2000
			{
				UsbPortInf = new UsbPortINFFile();
				UsbPortInf.ReadPackedCriticalFileFromDirectory(SetupDirectory);
			}
		}

		public void SaveModifiedINIFiles()
		{
			if (TextSetupInf.IsModified)
			{
				TextSetupInf.SaveToDirectory(SetupDirectory);

				if (IsTargetContainsTemporaryInstallation)
				{
					TextSetupInf.SaveToDirectory(TargetDirectory);
					TextSetupInf.SaveToDirectory(BootDirectory);
				}
			}

			if (HiveSoftwareInf.IsModified)
				HiveSoftwareInf.SaveToDirectory(SetupDirectory);

			if (Is64Bit && HiveSoftware32Inf.IsModified)
				HiveSoftware32Inf.SaveToDirectory(SetupDirectory);

			if (HiveSystemInf.IsModified)
				HiveSystemInf.SaveToDirectory(SetupDirectory);

			if (!IsTargetContainsTemporaryInstallation && DOSNetInf.IsModified)
				// integration to installation media
				DOSNetInf.SaveToDirectory(SetupDirectory);

			if (NetGPCInf.IsModified)
				NetGPCInf.SavePackedToDirectory(SetupDirectory);

			if (NetPacketSchedulerInf.IsModified)
				NetPacketSchedulerInf.SavePackedToDirectory(SetupDirectory);

			if (NetPacketSchedulerAdapterInf.IsModified)
				NetPacketSchedulerAdapterInf.SavePackedToDirectory(SetupDirectory);

			if (NetTCPIPInf.IsModified)
				NetTCPIPInf.SavePackedToDirectory(SetupDirectory);

			if (HalInf.IsModified)
				HalInf.SavePackedToDirectory(SetupDirectory);

			if (UsbInf.IsModified)
				UsbInf.SavePackedToDirectory(SetupDirectory);

			if (UsbStorageClassDriverInf.IsModified)
				UsbStorageClassDriverInf.SavePackedToDirectory(SetupDirectory);

			if (!IsWindows2000 && UsbPortInf.IsModified)
				UsbPortInf.SavePackedToDirectory(SetupDirectory);
		}

		public void SaveRegistryChanges()
		{
			SetupRegistryHive.UnloadHive(true);
			if (IsTargetContainsTemporaryInstallation)
			{
				FileSystemUtils.ClearReadOnlyAttribute(BootDirectory + SetupRegistryHiveFile.FileName);
				try
				{
					ProgramUtils.CopyCriticalFile(SetupDirectory + SetupRegistryHiveFile.FileName,
						BootDirectory + SetupRegistryHiveFile.FileName);
				}
				catch
				{
					Console.WriteLine("Error: failed to copy '{0}' to '{1}' (setup boot folder)", SetupRegistryHiveFile.FileName,
						BootDirectory);
					Program.Exit();
				}
			}
		}

		/// <summary>
		/// this method will delete migrate.inf, which contains current drive letter assignments.
		/// this step will assure that the system drive letter will be C
		/// </summary>
		public void DeleteMigrationInformation()
		{
			var path = BootDirectory + "migrate.inf";
			if (File.Exists(path))
			{
				FileSystemUtils.ClearReadOnlyAttribute(path);
				File.Delete(path);
			}
		}

		public void CopyFileFromSetupDirectoryToBootDirectory(string fileName)
		{
			FileSystemUtils.ClearReadOnlyAttribute(BootDirectory + fileName);
			File.Copy(SetupDirectory + fileName, BootDirectory + fileName, true);
		}

		public string GetSetupDriverDirectoryPath(string relativeDriverDirectoryPath)
		{
			var driverSourceDirectory = SetupDirectory + ScsiDriversSourceDirectory + relativeDriverDirectoryPath;
			return driverSourceDirectory;
		}

		/// <summary>
		/// Media root has the form of \i386\DRIVERS\BUSDRV
		/// </summary>
		/// /// <param name="relativeDriverDirectoryPath">Source driver directory relative to \Windows</param>
		public string GetSourceDriverDirectoryInMediaRootForm(string relativeDriverDirectoryPath)
		{
			var driverDirectoryInMediaRootForm = "\\" + SetupDirectoryName + "\\" + ScsiDriversSourceDirectory +
			                                     relativeDriverDirectoryPath;
			driverDirectoryInMediaRootForm = driverDirectoryInMediaRootForm.TrimEnd('\\');
			return driverDirectoryInMediaRootForm.ToLower();
		}

		/// <summary>
		/// has the form of Driver Cache\i386\
		/// </summary>
		/// <param name="relativeDriverDirectoryPath">Target driver directory relative to \Windows</param>
		public string GetDriverDestinationWinntDirectory(string relativeDriverDirectoryPath)
		{
			var driverTargetDirectoryWinnt = ScsiDriversDestinationDirectory + relativeDriverDirectoryPath;
			driverTargetDirectoryWinnt = driverTargetDirectoryWinnt.TrimEnd('\\');
			return driverTargetDirectoryWinnt;
		}

		public void CopyFileToSetupDriverDirectory(string sourceFilePath, string destinationRelativeDirectoryPath,
			string destinationFileName)
		{
			var destinationDirectoryPath = GetSetupDriverDirectoryPath(destinationRelativeDirectoryPath);
			FileSystemUtils.CreateDirectory(destinationDirectoryPath);
			ProgramUtils.CopyCriticalFile(sourceFilePath, destinationDirectoryPath + destinationFileName);
		}

		// Drivers (.sys) are needed to be copied to the setup dir and boot dir as well
		public void CopyDriverToSetupRootDirectory(string sourceFilePath, string fileName)
		{
			ProgramUtils.CopyCriticalFile(sourceFilePath, SetupDirectory + fileName);
		}

		public void AddDriverToBootDirectory(string sourceFilePath, string fileName)
		{
			ProgramUtils.CopyCriticalFile(sourceFilePath, SetupDirectory + fileName);
		}

		public string AllocateVirtualDeviceInstanceID(string deviceClassName)
		{
			// we will return the larger deviceInstanceID, we don't want to overwrite existing hivesys.inf device instances
			var deviceInstanceID1 = SetupRegistryHive.AllocateVirtualDeviceInstanceID(deviceClassName);
			var deviceInstanceID2 = HiveSystemInf.AllocateVirtualDeviceInstanceID(deviceClassName);
			
			// string comparison, note that both strings has fixed length with leading zeros
			return string.CompareOrdinal(deviceInstanceID1, deviceInstanceID2) == 1
				? deviceInstanceID1
				: deviceInstanceID2;
		}

		public TextSetupINFFile TextSetupInf { get; private set; }

		public HiveSoftwareINFFile HiveSoftwareInf { get; private set; }

		public HiveSoftware32INFFile HiveSoftware32Inf { get; private set; }

		public HiveSystemINFFile HiveSystemInf { get; private set; }

		public DOSNetINFFile DOSNetInf { get; private set; }

		public SetupRegistryHiveFile SetupRegistryHive { get; private set; }

		public NetGPCINFFile NetGPCInf { get; private set; }

		public NetPacketSchedulerINFFile NetPacketSchedulerInf { get; private set; }

		public NetPacketSchedulerAdapterINFFile NetPacketSchedulerAdapterInf { get; private set; }

		public NetTCPIPINFFile NetTCPIPInf { get; private set; }

		public HALINFFile HalInf { get; private set; }

		public UsbINFFile UsbInf { get; private set; }

		public UsbStorageClassDriverINFFile UsbStorageClassDriverInf { get; private set; }

		public UsbPortINFFile UsbPortInf { get; private set; }

		public bool IsTargetContainsTemporaryInstallation
		{
			get { return (BootDirectory != string.Empty); }
		}

		public bool IsTargetValid
		{
			get { return _localSourceDirectory != string.Empty; }
		}

		public string TargetDirectory { get; private set; }

		public string BootDirectory { get; private set; }

		public string SetupDirectory
		{
			get { return _localSourceDirectory + SetupDirectoryName + "\\"; }
		}

		public bool Is64Bit
		{
			get { return (ArchitectureIdentifier != "x86"); }
		}

		public string ArchitectureIdentifier
		{
			get
			{
				if (FileSystemUtils.IsDirectoryExist(_localSourceDirectory + "amd64"))
					return "amd64";

				if (FileSystemUtils.IsDirectoryExist(_localSourceDirectory + "IA64"))
					return "ia64";

				return "x86";
			}
		}

		private string SetupDirectoryName
		{
			get
			{
				if (FileSystemUtils.IsDirectoryExist(_localSourceDirectory + "amd64"))
					return "amd64";

				if (FileSystemUtils.IsDirectoryExist(_localSourceDirectory + "IA64"))
					return "IA64";

				return "I386";
			}
		}

		public bool IsWindows2000
		{
			get
			{
				return HiveSoftwareInf.GetWindowsProductName()
					.Equals("Microsoft Windows 2000", StringComparison.InvariantCultureIgnoreCase);
			}
		}

		public bool IsWindowsXP
		{
			get
			{
				return HiveSoftwareInf.GetWindowsProductName()
					.Equals("Microsoft Windows XP", StringComparison.InvariantCultureIgnoreCase);
			}
		}

		public bool IsWindowsServer2003
		{
			get
			{
				return HiveSoftwareInf.GetWindowsProductName()
					.Equals("Microsoft Windows Server 2003", StringComparison.InvariantCultureIgnoreCase);
			}
		}

		public int MinorOSVersion
		{
			get
			{
				if (IsWindowsServer2003 || (IsWindowsXP && Is64Bit))
					return 2; // Server 2003 and XP x64 has OS version 5.2

				return IsWindowsXP
					? 1  // XP x86 has OS version 5.1
					: 0; // Windows 2000 has OS version 5.0
			}
		}

		public int ServicePackVersion
		{
			get { return HiveSystemInf.GetWindowsServicePackVersion(); }
		}

		/// <summary>
		/// 0x0000001 (VER_NT_WORKSTATION) 
		/// 0x0000002 (VER_NT_DOMAIN_CONTROLLER) 
		/// 0x0000003 (VER_NT_SERVER) 
		/// </summary>
		public int ProductType
		{
			get
			{
				var productTypeString = HiveSystemInf.GetWindowsProductType();
				switch (productTypeString.ToLower())
				{
					case "winnt":
						return 1;
					case "lanmannt":
						return 2;
					case "servernt":
						return 3;
					default:
						return 1;
				}
			}
		}
	}
}