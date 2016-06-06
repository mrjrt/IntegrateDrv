using System;
using System.Collections.Generic;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.DeviceService;
using IntegrateDrv.HardwareDetectors;
using IntegrateDrv.Interfaces;
using IntegrateDrv.PNPDriver;
using IntegrateDrv.Utilities.Generics;
using IntegrateDrv.Utilities.PortableExecutable;
using IntegrateDrv.Utilities.Strings;
using IntegrateDrv.WindowsDirectory;
using Microsoft.Win32;

namespace IntegrateDrv.Integrators
{
	public class PNPDriverIntegrator : PNPDriverIntegratorBase
	{
		readonly WindowsInstallation _installation;
		private readonly bool _useLocalHardwareConfig;
		private readonly string _enumExportPath;
		readonly bool _preconfigure;

		private string _classInstanceID = string.Empty;

		// used to prevent collisions in text-mode (Key is the old file name, Value is the new file name)
		private readonly KeyValuePairList<string, string> _oldToNewFileName = new KeyValuePairList<string, string>();
		
		public PNPDriverIntegrator(PNPDriverDirectory driverDirectory, WindowsInstallation installation, string hardwareID, bool useLocalHardwareConfig, string enumExportPath, bool preconfigure)
			: base(driverDirectory, installation.ArchitectureIdentifier, installation.MinorOSVersion, installation.ProductType, hardwareID)
		{
			_installation = installation;
			_useLocalHardwareConfig = useLocalHardwareConfig;
			_enumExportPath = enumExportPath;
			_preconfigure = preconfigure;
		}

		public void IntegrateDriver()
		{
			PNPDriverINFFile pnpDriverInf;
			var installSectionName = DriverDirectory.GetDeviceInstallSectionName(HardwareID, _installation.ArchitectureIdentifier, _installation.MinorOSVersion, _installation.ProductType, out pnpDriverInf);
			if (installSectionName == string.Empty)
			{
				Console.WriteLine("Unable to locate InstallSectionName in INF file");
				Program.Exit();
			}

			_classInstanceID = _installation.SetupRegistryHive.AllocateClassInstanceID(pnpDriverInf.ClassGUID);

			ProcessInstallSection(pnpDriverInf, installSectionName, _classInstanceID);
			ProcessInstallServicesSection(pnpDriverInf, installSectionName);
			// this.DeviceServices is now populated

			if (DeviceServices.Count == 0)
			{
				Console.WriteLine("Error: driver does not have an associated service, IntegrateDrv will not proceed.");
				Program.Exit();
			}

			PrepareToPreventTextModeDriverNameCollision(DeviceServices);

			foreach (var deviceService in DeviceServices)
			{
				InstructToLoadTextModeDeviceService(deviceService);
				RegisterDeviceService(_installation.SetupRegistryHive, pnpDriverInf, deviceService);
				RegisterDeviceService(_installation.HiveSystemInf, pnpDriverInf, deviceService);
			}

			CopyDriverFiles(DeviceServices);

			// register the device:

			if (PNPDriverINFFile.IsRootDevice(HardwareID))
			{
				// installing virtual device: (this is critical for some services such as iScsiPrt)
				var virtualDeviceInstanceID = _installation.AllocateVirtualDeviceInstanceID(pnpDriverInf.ClassName);
				if (DeviceServices.Count > 0)
				{
					var deviceService = DeviceServices[0];
					PreconfigureDeviceInstance(pnpDriverInf, "Root", pnpDriverInf.ClassName.ToUpper(), virtualDeviceInstanceID, deviceService);
				}
			}
			else // physical device 
			{
				RegisterPhysicalDevice(pnpDriverInf);

				// GUI-Mode setup will scan all of the directories listed under "DevicePath" directories, 
				// if it will find multiple matches, it will use the .inf file that has the best match.
				// Microsoft does not define exactly how matching drivers are ranked, observations show that:
				// 1. When both .inf have the exact same hardwareID, and one of the .inf is signed and the other is not, the signed .inf will qualify as the best match.
				// 2. When both .inf have the exact same hardwareID, and both of the .inf files are unsigned, the .inf with the most recent version / date will qualify as the best match.
				// 3. When both .inf have the exact same hardwareID, and both of the .inf files are unsigned, and both has the same version / date, the .inf from the first directory listed under "DevicePath" will qualify as the best match.

				// We have to disable the device drivers included in windows to qualify the newly integrated drivers as best match:
				PNPDriverGUIModeIntegrator.DisableInBoxDeviceDrivers(_installation.SetupDirectory, _installation.ArchitectureIdentifier, _installation.MinorOSVersion, _installation.ProductType, HardwareID);
			}

			// Network Device:
			// We want to make the NIC driver accessible to windows GUI mode setup, otherwise no 'Network Connection' will be installed and TCP/IP configuration
			// for the NIC will be deleted. (and as a result, the NIC would not have TCP/IP bound to it)

			// Devices in general:
			// Windows will clear all existing Enum and / or Control\Class entries of devices that have no matching driver available during GUI-mode setup
			// (it will be done near the very end of GUI-mode setup)
			// So we let Windows GUI-Mode install the device.

			// Note: the driver will be modified for boot start
			var guiModeIntegrator = new PNPDriverGUIModeIntegrator(DriverDirectory, _installation, HardwareID);
			guiModeIntegrator.Integrate();
		}

		private void RegisterPhysicalDevice(PNPDriverINFFile pnpDriverInf)
		{
			if (_preconfigure && pnpDriverInf.IsNetworkAdapter && (_useLocalHardwareConfig || _enumExportPath != string.Empty))
			{
				string deviceID;
				string deviceInstanceID;

				if (_useLocalHardwareConfig)
				{
					deviceInstanceID = PNPLocalHardwareDetector.DetectLocalDeviceInstanceID(HardwareID, out deviceID);
					if (deviceInstanceID == string.Empty)
						Console.WriteLine(
							"Warning: Could not detect matching device installed locally, configuration will not be applied!");
				}
				else // m_enumExportPath != string.Empty
				{
					deviceInstanceID = PNPExportedHardwareDetector.DetectExportedDeviceInstanceID(_enumExportPath, HardwareID, out deviceID);
					if (deviceInstanceID == string.Empty)
						Console.WriteLine(
							"Warning: Could not detect matching device in the exported registry, configuration will not be applied!");
				}

				if (deviceInstanceID != string.Empty)
				{
					// m_netDeviceServices is now populated
					if (NetworkDeviceServices.Count > 0)
					{
						// unlike other types of hardware (SCSI controllers etc.), it's not enough to add a NIC to the 
						// Criticla Device Database (CDDB) to make it usable during boot, as mentioned in the comments above RegisterNicAsCriticalDevice()
						// at the very least, a CDDB entry and a "Device" registry value under Enum\Enumerator\DeviceID\DeviceInstanceID is required
						// (as well as DeviceDesc if not automatically added by the kernel-PNP)
						// here we manually register the hardware in advance, but it's better to use NICBootConf to do this during boot,
						// NICBootConf will also work if the NIC has been moved to another PCI slot since creating the installation media.

						// the first item in m_netDeviceServices should be the actual NIC (CHECKME: what about NIC / bus driver combination like nVIdia)
						var deviceService = NetworkDeviceServices[0];
						var enumerator = PNPDriverIntegratorUtils.GetEnumeratorNameFromHardwareID(HardwareID);
						PreconfigureDeviceInstance(pnpDriverInf, enumerator, deviceID, deviceInstanceID, deviceService);
					}
					else
						Console.WriteLine(
							"Warning: failed to install '{0}', because the service for this network adapter has not been registered!",
							HardwareID);
				}
			}
			else
			{
				// if it's a NIC, We assume the user will integrate NICBootConf, which will configure the network adapter during boot.
				// we'll just add the device to the Criticla Device Database (CDDB), and let kernel-PNP and NICBootConf do the rest.
				if (pnpDriverInf.IsNetworkAdapter)
				{
					// NICBootConf needs the ClassGUID in place for each DeviceInstance Key,
					// if we put the ClassGUID in the CDDB, the ClassGUID will be applied to each DeviceInstance with matching hardwareID
					_installation.TextSetupInf.AddDeviceToCriticalDeviceDatabase(HardwareID, DeviceServices[0].ServiceName, PNPDriverINFFile.NetworkAdapterClassGUID);
					_installation.HiveSystemInf.AddDeviceToCriticalDeviceDatabase(HardwareID, DeviceServices[0].ServiceName, PNPDriverINFFile.NetworkAdapterClassGUID);
				}
				else
				{
					_installation.TextSetupInf.AddDeviceToCriticalDeviceDatabase(HardwareID, DeviceServices[0].ServiceName);
					_installation.HiveSystemInf.AddDeviceToCriticalDeviceDatabase(HardwareID, DeviceServices[0].ServiceName);
				}
			}
		}

		// unlike other types of hardware (SCSI controllers etc.), it's not enough to add a NIC to the 
		// Criticla Device Database (CDDB) to make it usable during boot (Note that NIC driver is an NDIS
		// miniport driver, and the driver does not have an AddDevice() routine and instead uses NDIS' AddDevice())
		// This method performs the additional steps needed for a NIC that is added to the CDDB, which are basically letting Windows
		// know which device class instance is related to the device (TCP/IP settings are tied to the device class instance)
		// The above is true for both text-mode and GUI-mode / Final Windows.
		// Note: it's best to use a driver that does these steps during boot, I have written NICBootConf for that purpose.
		/*
		private void PreconfigureCriticalNetworkAdapter(PNPDriverINFFile pnpDriverInf, string enumerator, string deviceID, string deviceInstanceID, DeviceService deviceService)
		{
			string keyName = @"ControlSet001\Enum\" + enumerator + @"\" + deviceID + @"\" + deviceInstanceID;
			m_installation.SetupRegistryHive.SetRegistryKey(keyName, "Driver", RegistryValueKind.String, pnpDriverInf.ClassGUID + @"\" + m_classInstanceID);
			// The presence of DeviceDesc is critical for some reason, but any value can be used
			m_installation.SetupRegistryHive.SetRegistryKey(keyName, "DeviceDesc", RegistryValueKind.String, deviceService.DeviceDescription);

			// not critical:
			m_installation.SetupRegistryHive.SetRegistryKey(keyName, "ClassGUID", RegistryValueKind.String, pnpDriverInf.ClassGUID);

			// we must not specify ServiceName or otherwise kernel-PNP will skip this device

			// let kernel-PNP take care of the rest for us, ClassGUID is not critical:
			m_installation.TextSetupInf.AddDeviceToCriticalDeviceDatabase(this.HardwareID, deviceService.ServiceName);
		}
		*/

		/// <summary>
		/// When using this method, there is no need to use the Critical Device Database
		/// </summary>
		private void PreconfigureDeviceInstance(PNPDriverINFFile pnpDriverInf, string enumerator, string deviceID, string deviceInstanceID, BaseDeviceService deviceService)
		{
			PreconfigureDeviceInstance(pnpDriverInf, _installation.SetupRegistryHive, enumerator, deviceID, deviceInstanceID, deviceService);
			// Apparently this is not necessary for the devices to work properly in GUI-mode, because configuration will stick from text-mode setup:
			PreconfigureDeviceInstance(pnpDriverInf, _installation.HiveSystemInf, enumerator, deviceID, deviceInstanceID, deviceService);
		}

		private void PreconfigureDeviceInstance(PNPDriverINFFile pnpDriverInf, ISystemRegistryHive systemRegistryHive, string enumerator, string deviceID, string deviceInstanceID, BaseDeviceService deviceService)
		{
			var driver = pnpDriverInf.ClassGUID.ToUpper() + @"\" + _classInstanceID;
			var manufacturerName = pnpDriverInf.GetDeviceManufacturerName(HardwareID, _installation.ArchitectureIdentifier, _installation.MinorOSVersion, _installation.ProductType);

			var hardwareKeyName = @"Enum\" + enumerator + @"\" + deviceID + @"\" + deviceInstanceID;

			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "ClassGUID", RegistryValueKind.String, pnpDriverInf.ClassGUID);
			// The presence of DeviceDesc is critical for some reason, but any value can be used
			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "DeviceDesc", RegistryValueKind.String, deviceService.DeviceDescription);
			// "Driver" is used to help Windows determine which software key belong to this hardware key.
			// Note: When re-installing the driver, the software key to be used will be determined by this value as well.
			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "Driver", RegistryValueKind.String, driver);
			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "Service", RegistryValueKind.String, deviceService.ServiceName);

			// ConfigFlags is not related to the hardware, it's the status of the configuration of the device by Windows (CONFIGFLAG_FAILEDINSTALL etc.)
			// the presence of this value tells windows the device has driver installed
			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "ConfigFlags", RegistryValueKind.DWord, 0);

			if (PNPDriverINFFile.IsRootDevice(HardwareID))
				// Windows uses the "HardwareID" entry to determine if the hardware is already installed,
				// We don't have to add this value for physical devices, because Windows will get this value from the device,
				// but we must add this for virtual devices, or we will find ourselves with duplicity when re-installing (e.g. two Microsoft iScsi Initiators).
				systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "HardwareID", RegistryValueKind.MultiString,
					new[] {HardwareID});

			// not necessary:
			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "Mfg", RegistryValueKind.String, manufacturerName);
			systemRegistryHive.SetCurrentControlSetRegistryKey(hardwareKeyName, "Class", RegistryValueKind.String, pnpDriverInf.ClassName);
		}

		// An explanation about driver name collision in text-mode:
		// in text-mode, the serviceName will be determined by the name of the file, and AFAIK there is no way around it,
		// so in order for a driver such as the Microsoft-provided Intel E1000 to work properly (service name: E1000, filename: e1000325.sys),
		// we are left with two choices:
		// 1. create the registry entries in the wrong place, e.g. under Services\serviceFileName-without-sys-extension (e.g. Services\e1000325)
		// 2. rename the serviceFileName to match the correct serviceName (e.g. e1000325.sys becomes E1000.sys)
		// (there is also a third option to install the service under both names - but it's a really messy proposition)

		// the first option will work for text-mode, but will break the GUI mode later (CurrentControlSet\Enum entries will be incorrect,
		// and trying to overwrite them with hivesys.inf will not work AFAIK).
		// so we are left with the second option, it's easy to do in the case of the Intel E1000, we will simply use E1000.sys for text-mode,
		// the problem is when there is already a dependency that uses the file name we want to use, 
		// the perfect example is Microsoft iSCSI initiator (service name: iScsiPrt, service filename: msiscsi.sys, dependency: iscsiprt.sys),
		// we want to rename msiscsi.sys to iscsiprt.sys, but there is a collision, because iscsiprt.sys is already taken by a necessary dependency,
		// the solution is to rename iscsiprt.sys to a different name (iscsip_o.sys), and patch(!) msiscsi.sys (which now becomes iscsiprt.sys) to use the new dependency name.

		// this method will test if there is a collision we will need to take care of later, and populate the needed variables.
		/// <param name="deviceServices"></param>
		private void PrepareToPreventTextModeDriverNameCollision(List<BaseDeviceService> deviceServices)
		{
			var serviceFileNames = new List<string>();
			var expectedServiceFileNames = new List<string>();
			foreach (var deviceService in deviceServices)
			{
				serviceFileNames.Add(deviceService.FileName);
				expectedServiceFileNames.Add(deviceService.TextModeFileName);
			}

			// we put the filenames with a name matching the service executable at the top
			var insertIndex = 0;
			for (var index = 0; index < DriverFilesToCopy.Count; index++)
			{
				var fileName = DriverFilesToCopy[index].DestinationFileName;
				if (StringUtils.ContainsCaseInsensitive(serviceFileNames, fileName))
				{
					var serviceExecutableEntry = DriverFilesToCopy[index];
					DriverFilesToCopy.RemoveAt(index);
					DriverFilesToCopy.Insert(insertIndex, serviceExecutableEntry);
					insertIndex++;
				}
			}
			// now the service executables are at the top
			for (var index = insertIndex; index < DriverFilesToCopy.Count; index++)
			{
				var fileName = DriverFilesToCopy[index].DestinationFileName;
				var collisionIndex = StringUtils.IndexOfCaseInsensitive(expectedServiceFileNames, fileName);
				if (collisionIndex >= 0)
				{
					var serviceName = deviceServices[collisionIndex].ServiceName;
					var newFileName = serviceName.Substring(0, serviceName.Length - 2) + "_o.sys";
					_oldToNewFileName.Add(fileName, newFileName);
					Console.WriteLine("Using special measures to prevent driver naming collision");
				}
			}
		}

		// see comments above PrepareToPreventTextModeDriverNameCollision() ^^
		/// <summary>
		/// Will copy PNP driver files to setup and boot directories, and update txtsetup.inf accordingly.
		/// The modifications support 3 different installation scenarions: 
		/// 1.  The user install using unmodified CD, use this program to integrate the drivers to the temporary installation folder that was created and then boot from it.
		/// 2.  The user uses this program to create modified installation folder / CD, boots from Windows PE
		///	 at the target machine, and use winnt32.exe to install the target OS. (DOS / winnt.exe should work too)
		/// 3. The user uses this program to create modified installation CD and boot from it.
		/// Note: We do not support RIS (seems too complex and can collide with our own TCP/IP integration)
		/// </summary>
		private void CopyDriverFiles(List<BaseDeviceService> deviceServices)
		{
			var serviceFileNames = new List<string>();
			foreach (var deviceService in deviceServices)
				serviceFileNames.Add(deviceService.FileName);

			for (var index = 0; index < DriverFilesToCopy.Count; index++)
			{
				var sourceFilePath = DriverDirectory.Path + DriverFilesToCopy[index].RelativeSourceFilePath;
				var fileName = DriverFilesToCopy[index].DestinationFileName;
				var serviceWithNameCollision = false;

				string textModeFileName;
				if (fileName.ToLower().EndsWith(".sys"))
				{
					var serviceIndex = StringUtils.IndexOfCaseInsensitive(serviceFileNames, fileName);
					if (serviceIndex >= 0)
					{
						var serviceName = deviceServices[index].ServiceName;
						textModeFileName = deviceServices[index].TextModeFileName;
						serviceWithNameCollision = StringUtils.ContainsCaseInsensitive(_oldToNewFileName.Keys, textModeFileName);

						if (serviceName.Length > 8 && !_installation.IsTargetContainsTemporaryInstallation)
						{
							Console.WriteLine("Warning: Service '{0}' has name longer than 8 characters.", serviceName);
							Console.Write("********************************************************************************");
							Console.Write("*You must use ISO level 2 compatible settings if you wish to create a working  *");
							Console.Write("*bootable installation CD.													 *");
							Console.Write("*if you're using nLite, choose mkisofs over the default ISO creation engine.   *");
							Console.Write("********************************************************************************");
						}
					}
					else
					{
						var renameIndex = StringUtils.IndexOfCaseInsensitive(_oldToNewFileName.Keys, fileName);
						textModeFileName = renameIndex >= 0
							? _oldToNewFileName[renameIndex].Value
							: fileName;
					}
				}
				else
					textModeFileName = fileName;

				if (fileName.ToLower().EndsWith(".sys") || fileName.ToLower().EndsWith(".dll"))
					// we copy all the  executables to the setup directory, Note that we are using textModeFileName
					// (e.g. e1000325.sys becomes E1000.sys) this is necessary for a bootable cd to work properly)
					// but we have to rename the file during text-mode copy phase for GUI-mode to work properly
					ProgramUtils.CopyCriticalFile(sourceFilePath, _installation.SetupDirectory + textModeFileName, true);

				// see comments above PrepareToPreventTextModeDriverNameCollision() ^^
				// in case of a service name collision, here we patch the service executable file that we just copied and update the name of its dependency
				if (serviceWithNameCollision)
				{
					// we need the renamed patched file in the setup (e.g. I386 folder) for a bootable cd to work properly
					PreventTextModeDriverNameCollision(_installation.SetupDirectory + textModeFileName);

					// we need the original file too (for GUI-mode)
					ProgramUtils.CopyCriticalFile(sourceFilePath, _installation.SetupDirectory + fileName);
				}

				// update txtsetup.sif:
				if (fileName.ToLower().EndsWith(".sys"))
				{
					// this is for the GUI-mode, note that we copy the files to their destination using their original name,
					// also note that if there is a collision we copy the original (unpatched) file instead of the patched one.
					if (serviceWithNameCollision)
					{
						// this is the unpatched file:
						_installation.TextSetupInf.SetSourceDisksFileDriverEntry(
							_installation.ArchitectureIdentifier,
							fileName,
							FileCopyDisposition.AlwaysCopy,
							fileName);

						// this is the patched file, we're not copying it anywhere, but we load this service executable so text-mode setup demand an entry (probably to locate the file source directory)
						_installation.TextSetupInf.SetSourceDisksFileDriverEntry(
							_installation.ArchitectureIdentifier,
							textModeFileName,
							FileCopyDisposition.DoNotCopy);
					}
					else
						_installation.TextSetupInf.SetSourceDisksFileDriverEntry(
							_installation.ArchitectureIdentifier,
							textModeFileName,
							FileCopyDisposition.AlwaysCopy,
							fileName);
				}
				else if (fileName.ToLower().EndsWith(".dll"))
					_installation.TextSetupInf.SetSourceDisksFileDllEntry(_installation.ArchitectureIdentifier, fileName);
				// finished updating txtsetup.sif

				if (_installation.IsTargetContainsTemporaryInstallation)
				{
					if (fileName.ToLower().EndsWith(".sys"))
						// we copy all drivers by their text-mode name
						ProgramUtils.CopyCriticalFile(
							_installation.SetupDirectory + textModeFileName,
							_installation.BootDirectory + textModeFileName);
				}
				else
				{
					// update dosnet.inf
					if (fileName.ToLower().EndsWith(".sys"))
					{
						// we already made sure all the files in the setup directory are using their textModeFileName
						_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToLocalSourceRootDirectory(textModeFileName, textModeFileName);
						_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory(textModeFileName, textModeFileName);

						if (serviceWithNameCollision)
							// the unpatched .sys should be available with it's original (GUI) name in the \$WINNT$.~LS folder
							_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToLocalSourceRootDirectory(fileName);
					}
					else if (fileName.ToLower().EndsWith(".dll"))
						// in the case of .dll fileName is the same as textModeFileName
						_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToLocalSourceRootDirectory(fileName);
				}
			}
		}

		private void PreventTextModeDriverNameCollision(string filePath)
		{
			var serviceNameCollisionDetected = (_oldToNewFileName.Count > 0);
			if (serviceNameCollisionDetected)
			{
				var dependencies = PortableExecutableUtils.GetDependencies(filePath);
				for(var index = 0; index < _oldToNewFileName.Count; index++)
				{
					var oldFileName = _oldToNewFileName[index].Key;
					var newFileName = _oldToNewFileName[index].Value;
					if (dependencies.Contains(oldFileName)) // this happens for iscsiprt / msiscsi

						PortableExecutableUtils.RenameDependencyFileName(filePath, oldFileName, newFileName);
				}
			}
		}

		protected override void SetCurrentControlSetRegistryKey(string keyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			// text-mode
			_installation.SetupRegistryHive.SetCurrentControlSetRegistryKey(keyName, valueName, valueKind, valueData);
			// GUI-mode
			_installation.HiveSystemInf.SetCurrentControlSetRegistryKey(keyName, valueName, valueKind, valueData);
		}

		private void InstructToLoadTextModeDeviceService(BaseDeviceService deviceService)
		{
			// update txtsetup.sif
			if (deviceService.ServiceGroup == string.Empty)
				// No group, which means txtsetup.sif will have effect on initialization order.
				// In final Windows this means the service is initialized after all other services.
				// To do the same in text-mode, we should load this service last (which means using the [CdRomDrivers.Load] section):
				_installation.TextSetupInf.InstructToLoadCdRomDriversDriver(
					deviceService.TextModeFileName,
					deviceService.DeviceDescription);
			else
				// we have set a group in setupreg.hiv, so for text-mode it doesn't matter where we put the service in txtsetup.sif,
				// however, some of the [xxxx.Load] groups will stick and cause problems later (GUI-mode / final Windows),
				// see TextSetupINFFile.Load.cs to see which groups may cause problems
				//
				// Note that the service is renamed back to its original name if necessary.
				_installation.TextSetupInf.InstructToLoadKeyboardDriver(
					deviceService.TextModeFileName,
					deviceService.DeviceDescription);
		}

		private void RegisterDeviceService(ISystemRegistryHive systemRegistryHive, PNPDriverINFFile pnpDriverInf, BaseDeviceService deviceService)
		{
			// We ignore start type. if the user uses this program, she wants to boot something!
			const int startType = 0;
			// Note: using a different service registry key under CurrentControlSet\Services with an ImagePath entry referring to the .sys will not work in text mode setup!
			// Text-mode setup will always initialize services based on the values stored under Services\serviceName, where serviceName is the service file name without the .sys extension.

			// write all to registry:
			var serviceName = deviceService.ServiceName;
			if (deviceService.ServiceDisplayName != string.Empty)
				systemRegistryHive.SetServiceRegistryKey(
					serviceName,
					string.Empty,
					"DisplayName",
					RegistryValueKind.String,
					deviceService.ServiceDisplayName);
			systemRegistryHive.SetServiceRegistryKey(serviceName, string.Empty, "ErrorControl", RegistryValueKind.DWord, deviceService.ErrorControl);
			if (deviceService.ServiceGroup != string.Empty)
				systemRegistryHive.SetServiceRegistryKey(
					serviceName,
					string.Empty,
					"Group",
					RegistryValueKind.String,
					deviceService.ServiceGroup);
			systemRegistryHive.SetServiceRegistryKey(serviceName, string.Empty, "Start", RegistryValueKind.DWord, startType);
			systemRegistryHive.SetServiceRegistryKey(serviceName, string.Empty, "Type", RegistryValueKind.DWord, deviceService.ServiceType);

			if (systemRegistryHive is HiveSystemINFFile) // GUI Mode registry
				systemRegistryHive.SetServiceRegistryKey(
					serviceName,
					string.Empty,
					"ImagePath",
					RegistryValueKind.String,
					deviceService.ImagePath);

			// Note that software key will stick from text-mode:
			var softwareKeyName = @"Control\Class\" + pnpDriverInf.ClassGUID + @"\" + _classInstanceID;

			var service = deviceService as NetworkDeviceService;
			if (service != null)
			{
				var netCfgInstanceID = service.NetCfgInstanceID;
				// - sanbootconf and iScsiBP use this value, but it's not necessary for successful boot, static IP can be used instead.
				// - the presence of this value will stick and stay for the GUI mode
				// - the presence of this value during GUI Mode will prevent the IP settings from being resetted
				// - the presence of this value will cause Windows 2000 \ XP x86 to hang after the NIC driver installation (there is no problem with Windows Server 2003)
				// - the presence of this value will cause Windows XP x64 to hang during the "Installing Network" phase (there is no problem with Windows Server 2003)

				// we will set this value so sanbootconf / iScsiBP could use it, and if necessary, delete it before the NIC driver installation (using hal.inf)
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "NetCfgInstanceId", RegistryValueKind.String, netCfgInstanceID);
				if (!_installation.IsWindowsServer2003)
					// delete the NetCfgInstanceId registry value during the beginning of GUI-mode setup
					_installation.HalInf.DeleteNetCfgInstanceIdFromNetworkAdapterClassInstance(_classInstanceID);

				// The Linkage subkey is critical, and is used to bind the network adapter to TCP/IP:
				// - The NetCfgInstanceId here is the one Windows actually uses for TCP/IP configuration.
				// - The first component in one entry corresponds to the first component in the other entries:
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "Linkage", "Export", RegistryValueKind.MultiString, new[] { @"\Device\" + netCfgInstanceID });
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "Linkage", "RootDevice", RegistryValueKind.MultiString, new[] { netCfgInstanceID }); // Windows can still provide TCP/IP without this entry
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "Linkage", "UpperBind", RegistryValueKind.MultiString, new[] { "Tcpip" });
			}

			// We need to make sure the software key is created, otherwise two devices can end up using the same software key

			// Note for network adapters:
			// "MatchingDeviceId" is not critical for successfull boot or devices which are not network adapters, but it's critical for NICBootConf in case it's being used
			// Note: Windows will store the hardwareID as it appears in the driver, including &REV
			systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "MatchingDeviceId", RegistryValueKind.String, HardwareID.ToLower());

			// not necessary. in addition, it will also be performed by GUI-mode setup
			if (deviceService.DeviceDescription != string.Empty)
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "DriverDesc", RegistryValueKind.String,
					deviceService.DeviceDescription);
			if (pnpDriverInf.DriverVersion != string.Empty)
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "DriverVersion", RegistryValueKind.String,
					pnpDriverInf.DriverVersion);
			if (pnpDriverInf.Provider != string.Empty)
				systemRegistryHive.SetCurrentControlSetRegistryKey(softwareKeyName, "ProviderName", RegistryValueKind.String,
					pnpDriverInf.Provider);
		}
	}
}
