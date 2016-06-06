using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.PNPDriver;
using IntegrateDrv.Utilities.FileSystem;
using IntegrateDrv.Utilities.PortableExecutable;
using IntegrateDrv.WindowsDirectory;
using Microsoft.Deployment.Compression;
using Microsoft.Deployment.Compression.Cab;
using Microsoft.Win32;

namespace IntegrateDrv.Integrators
{
	public class PNPDriverGUIModeIntegrator : PNPDriverIntegratorBase
	{
		readonly WindowsInstallation _installation;

		public PNPDriverGUIModeIntegrator(PNPDriverDirectory driverDirectory, WindowsInstallation installation, string hardwareID)
			: base(driverDirectory, installation.ArchitectureIdentifier, installation.MinorOSVersion, installation.ProductType, hardwareID)
		{
			_installation = installation;
		}

		public void Integrate()
		{
			PNPDriverINFFile pnpDriverInf;
			var installSectionName = DriverDirectory.GetDeviceInstallSectionName(HardwareID, _installation.ArchitectureIdentifier, _installation.MinorOSVersion, _installation.ProductType, out pnpDriverInf);
			ProcessInstallSection(pnpDriverInf, installSectionName, string.Empty); // We don't care about the classInstanceID because we don't populate the registry
			ProcessCoInstallersSection(pnpDriverInf, installSectionName);
			CopyDriverToSetupDriverDirectoryAndRegisterIt(pnpDriverInf);
		}

		private void CopyDriverToSetupDriverDirectoryAndRegisterIt(PNPDriverINFFile pnpDriverInf)
		{
			Console.WriteLine();
			Console.WriteLine("Making the driver available to GUI mode setup.");

			// build list of source files:
			var driverFiles = new List<string>();
			foreach (var fileToCopy in DriverFilesToCopy)
			{
				DisableInBoxDeviceDriverFile(_installation.SetupDirectory, fileToCopy.DestinationFileName);

				driverFiles.Add(fileToCopy.RelativeSourceFilePath);
			}
			
			// make sure the .inf file will be copied too
			driverFiles.Add(pnpDriverInf.FileName);

			if (pnpDriverInf.CatalogFile != string.Empty)
			{
				if (File.Exists(DriverDirectory.Path + pnpDriverInf.CatalogFile))
					// add the catalog file too (to suppress unsigned driver warning message if the .inf has not been modified)
					// the catalog file is in the same location as the INF file ( http://msdn.microsoft.com/en-us/library/windows/hardware/ff547502%28v=vs.85%29.aspx )
					driverFiles.Add(pnpDriverInf.CatalogFile);
			}

			// Note that we may perform some operations on the same directory more than once,
			// the allocate / register methods are supposed to return the previously allocated IDs on subsequent calls,
			// and skip registration of previously registered directories
			foreach (var relativeFilePath in driverFiles)
			{
				var fileName = FileSystemUtils.GetNameFromPath(relativeFilePath);
				var relativeDirectoryPath = relativeFilePath.Substring(0, relativeFilePath.Length - fileName.Length);

				// we need to copy the files to the proper sub-directories
				_installation.CopyFileToSetupDriverDirectory(DriverDirectory.Path + relativeDirectoryPath + fileName, HardwareID + @"\" + relativeDirectoryPath, fileName);

				var sourceDirectoryInMediaRootForm = _installation.GetSourceDriverDirectoryInMediaRootForm(HardwareID + @"\" + relativeDirectoryPath); // note that we may violate ISO9660 - & is not allowed
				var sourceDiskID = _installation.TextSetupInf.AllocateSourceDiskID(_installation.ArchitectureIdentifier, sourceDirectoryInMediaRootForm);

				var destinationWinntDirectory = _installation.GetDriverDestinationWinntDirectory(HardwareID + @"\" + relativeDirectoryPath);
				var destinationWinntDirectoryID = _installation.TextSetupInf.AllocateWinntDirectoryID(destinationWinntDirectory);

				_installation.TextSetupInf.SetSourceDisksFileEntry(_installation.ArchitectureIdentifier, sourceDiskID, destinationWinntDirectoryID, fileName, FileCopyDisposition.AlwaysCopy);
				
				// dosnet.inf: we add the file to the list of files to be copied to local source directory
				if (!_installation.IsTargetContainsTemporaryInstallation)
					_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToLocalSourceDriverDirectory(
						sourceDirectoryInMediaRootForm, fileName);

				_installation.HiveSoftwareInf.RegisterDriverDirectory(destinationWinntDirectory);
				if (_installation.Is64Bit)
					// hivsft32.inf
					_installation.HiveSoftware32Inf.RegisterDriverDirectory(destinationWinntDirectory);
			}

			// set inf to boot start:
			var setupDriverDirectoryPath = _installation.GetSetupDriverDirectoryPath(HardwareID + @"\"); // note that we may violate ISO9660 - & character is not allowed
			var installSectionName = pnpDriverInf.GetDeviceInstallSectionName(HardwareID, _installation.ArchitectureIdentifier, _installation.MinorOSVersion, _installation.ProductType);
			pnpDriverInf.SetServiceToBootStart(installSectionName, _installation.ArchitectureIdentifier, _installation.MinorOSVersion);
			pnpDriverInf.SaveToDirectory(setupDriverDirectoryPath);
			// finished setting inf to boot start
		}

		protected override void SetCurrentControlSetRegistryKey(string keyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			// Do nothing, we just copy files
		}

		// Windows File Protection may restore a newer unsigned driver file to an older in-box signed driver file (sfc.exe is executed at the end of GUI-mode setup).
		// The list of files that is being protected is stored in sfcfiles.sys, and we can prevent a file from being protected by making sure it's not in that list.
		private static void DisableInBoxDeviceDriverFile(string setupDirectory, string fileName)
		{
			fileName = fileName.ToLower(); // sfcfiles.dll stores all file names in lowercase
			var path = setupDirectory + "sfcfiles.dl_";
			var packed = File.ReadAllBytes(path);
			var unpacked = INIFile.Unpack(packed, "sfcfiles.dll");
			var peInfo = new PortableExecutableInfo(unpacked);
			var oldValue = @"%systemroot%\system32\drivers\" + fileName;
			var newValue = @"%systemroot%\system32\drivers\" + fileName.Substring(0, fileName.Length - 1) + "0"; // e.g. e1000325.sys => e1000325.sy0
			var oldSequence = Encoding.Unicode.GetBytes(oldValue);
			var newSequence = Encoding.Unicode.GetBytes(newValue);

			var replaced = false;
			for (var index = 0; index < peInfo.Sections.Count; index++)// XP uses the .text section while Windows 2000 uses the .data section
			{
				var section = peInfo.Sections[index];
				var replacedInSection = KernelAndHalIntegrator.ReplaceInBytes(ref section, oldSequence, newSequence);
				
				if (replacedInSection)
				{
					peInfo.Sections[index] = section;
					replaced = true;
				}
			}

			if (replaced)
			{
				Console.WriteLine();
				Console.WriteLine("'{0}' has been removed from Windows File Protection file list.", fileName);
				
				var peStream = new MemoryStream();
				PortableExecutableInfo.WritePortableExecutable(peInfo, peStream);
				unpacked = peStream.ToArray();
				packed = INIFile.Pack(unpacked, "sfcfiles.dll");

				FileSystemUtils.ClearReadOnlyAttribute(path);
				File.WriteAllBytes(path, packed);
			}
		}

		// In-box device drivers = drivers that are shipped with Windows
		public static void DisableInBoxDeviceDrivers(string setupDirectory, string architectureIdentifier, int minorOSVersion, int productType, string hardwareID)
		{
			Console.WriteLine();
			Console.WriteLine("Looking for drivers for your device (" + hardwareID + ") in Windows setup directory (to disable them):");
			var filePaths = Directory.GetFiles(setupDirectory, "*.in_");
			foreach (var filePath in filePaths)
			{
				var packedFileName = FileSystemUtils.GetNameFromPath(filePath);
				var unpackedFileName = packedFileName.Substring(0, packedFileName.Length - 1) + "F"; // the filename inside the archive ends with .INF and not with .IN_

				var cabInfo = new CabInfo(filePath);

				ArchiveFileInfo fileInfo = null;
				try
				{
					// some files do not contain an inf file
					// for instance, netmon.in_ contains netmon.ini
					fileInfo = cabInfo.GetFile(unpackedFileName);
				}
				catch (CabException ex)
				{
					// file is invalid / unsupported
					Console.WriteLine("Cannot examine file '{0}': {1}", packedFileName, ex.Message);
				}

				if (fileInfo != null)
				{
					var driverInf = new PNPDriverINFFile(unpackedFileName);
					try
					{
						driverInf.ReadPackedFromDirectory(setupDirectory);
					}
					catch (CabException ex)
					{
						// the archive is a cab and it contains the file we are looking for, but the file is corrupted
						Console.WriteLine("Cannot unpack file '{0}': {1}", driverInf.PackedFileName, ex.Message);
						continue;
					}

					// Windows will pick up it's own signed drivers even if the added driver also match the SUBSYS,
					// so we have to disable in-box drivers regardless of the presence of &SUBSYS
					var found = driverInf.DisableMatchingHardwareID(hardwareID, architectureIdentifier, minorOSVersion, productType);

					if (found)
					{
						Console.WriteLine("Device driver for '{0}' found in file '{1}'.", hardwareID, packedFileName);
						driverInf.SavePackedToDirectory(setupDirectory);
					}
				}
			}
			Console.WriteLine("Finished looking for drivers for your device in Windows setup directory.");
		}
	}
}
