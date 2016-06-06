using System;
using System.Collections.Generic;
using IntegrateDrv.DeviceService;
using IntegrateDrv.Interfaces;
using IntegrateDrv.Utilities.Conversion;
using IntegrateDrv.Utilities.Strings;
using IntegrateDrv.WindowsDirectory;
using Microsoft.Win32;

namespace IntegrateDrv.Integrators
{
	public class TCPIPIntegrator
	{
		private readonly WindowsInstallation _installation;
		private readonly List<NetworkDeviceService> _netDeviceServices;

		public TCPIPIntegrator(WindowsInstallation installation, List<NetworkDeviceService> netDeviceServices)
		{
			_installation = installation;
			_netDeviceServices = netDeviceServices;
		}

		public void SetTCPIPBoot()
		{
			PrepareTextModeToTCPIPBootStart();

			SetRegistryToTCPIPBootStart(_installation.SetupRegistryHive);
			SetRegistryToTCPIPBootStart(_installation.HiveSystemInf);

			SetTCPIPSetupToBootStartForGUIMode();
		}

		private void PrepareTextModeToTCPIPBootStart()
		{
			// Copy the necessary files:
			if (_installation.IsTargetContainsTemporaryInstallation)
			{
				_installation.CopyFileFromSetupDirectoryToBootDirectory("tdi.sy_"); // dependency of tcpip.sys
				_installation.CopyFileFromSetupDirectoryToBootDirectory("ndis.sy_"); // dependency of tcpip.sys
				if (!_installation.IsWindows2000) // Windows 2000 has an independent Tcpip service that does not require IPSec
					_installation.CopyFileFromSetupDirectoryToBootDirectory("ipsec.sy_");
				_installation.CopyFileFromSetupDirectoryToBootDirectory("tcpip.sy_");
			}
			else
			{
				// Update DOSNet.inf
				_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory("tdi.sy_");
				_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory("ndis.sy_");
				if (!_installation.IsWindows2000) // Windows 2000 has an independent Tcpip service that does not require IPSec
					_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory("ipsec.sy_");
				_installation.DOSNetInf.InstructSetupToCopyFileFromSetupDirectoryToBootDirectory("tcpip.sy_");
			}

			// update txtsetup.sif:

			// We must make sure NDIS is initialized before the NIC (does it have to be loaded before the NIC too?)

			// a solution to the above is to use the [BusExtender.Load] section, 
			// Since BusExtenders are loaded before almost everything else (setupdd.sys -> BootBusExtenders -> BusExtenders ...) it's a great place for NDIS,
			// another advantage of the [BusExtender.Load] section is that it's not sticky. (see TextSetupINFFile.Load.cs)
			
			_installation.TextSetupInf.InstructToLoadBusExtenderDriver("ndis.sys", "NDIS");

			// [DiskDrivers] is not sticky as well, we use [DiskDrivers], but it doesn't really matter because we specify group later
			if (!_installation.IsWindows2000) // Windows 2000 has an independent Tcpip service that does not require IPSec
				_installation.TextSetupInf.InstructToLoadDiskDriversDriver("ipsec.sys", "IPSEC Driver");
			_installation.TextSetupInf.InstructToLoadDiskDriversDriver("tcpip.sys", "TCP/IP Protocol Driver");
			
			// Note about critical files for iSCSI boot:
			// ksecdd.is critical
			// pci.sys is critical
			// partmgr.sys is critical (and its dependency wmilib.sys)
			// disk.sys is critical (and its dependency classpnp.sys)
		}

		/// <summary>
		/// Update the registry, set TCP/IP related services to boot start
		/// </summary>
		private void SetRegistryToTCPIPBootStart(ISystemRegistryHive systemRegistryHive)
		{
			const int serviceBootStart = 0;

			if (systemRegistryHive is SetupRegistryHiveFile)
			{
				// text-mode:
				_installation.SetupRegistryHive.SetServiceRegistryKey("KSecDD", string.Empty, "Start", RegistryValueKind.DWord, serviceBootStart);
				_installation.SetupRegistryHive.SetServiceRegistryKey("KSecDD", string.Empty, "Group", RegistryValueKind.String, "Base");

				_installation.SetupRegistryHive.SetServiceRegistryKey("NDIS", string.Empty, "Start", RegistryValueKind.DWord, serviceBootStart);
				_installation.SetupRegistryHive.SetServiceRegistryKey("NDIS", string.Empty, "Group", RegistryValueKind.String, "NDIS Wrapper");
			}
			// GUI-mode: KSecDD is already taken care of by default
			// GUI-mode: NDIS is already taken care of by default

			if (!_installation.IsWindows2000) // Windows 2000 has an independent Tcpip service that does not require IPSec
			{
				systemRegistryHive.SetServiceRegistryKey("IPSec", string.Empty, "Start", RegistryValueKind.DWord, serviceBootStart);
				systemRegistryHive.SetServiceRegistryKey("IPSec", string.Empty, "Group", RegistryValueKind.String, "PNP_TDI"); // this will be ignored, text-mode setup will assign Tcpip to 'SCSI Miniport' (because that's where we put it in txtsetup.sif)
				systemRegistryHive.SetServiceRegistryKey("IPSec", string.Empty, "Type", RegistryValueKind.DWord, 1); // SERVICE_KERNEL_DRIVER
			}

			systemRegistryHive.SetServiceRegistryKey("Tcpip", string.Empty, "Start", RegistryValueKind.DWord, serviceBootStart);
			systemRegistryHive.SetServiceRegistryKey("Tcpip", string.Empty, "Group", RegistryValueKind.String, "PNP_TDI"); // this will be ignored, text-mode setup will assign IPSec to 'SCSI Miniport' (because that's where we put it in txtsetup.sif)
			systemRegistryHive.SetServiceRegistryKey("Tcpip", string.Empty, "Type", RegistryValueKind.DWord, 1); // SERVICE_KERNEL_DRIVER
			if (!_installation.IsWindows2000) // Windows 2000 has an independent Tcpip service that does not require IPSec
				// not absolutely necessary apparently. however. it's still a good practice:
				systemRegistryHive.SetServiceRegistryKey("Tcpip", string.Empty, "DependOnService", RegistryValueKind.String, "IPSec");

			// Needed for stability
			systemRegistryHive.SetCurrentControlSetRegistryKey(@"Control\Session Manager\Memory Management", "DisablePagingExecutive", RegistryValueKind.DWord, 1);
			// not sure that's really necessary, but sanbootconf setup does it, so it seems like a good idea (1 by default for Server 2003)
			systemRegistryHive.SetServiceRegistryKey("Tcpip", "Parameters", "DisableDHCPMediaSense", RegistryValueKind.DWord, 1);

			var bootServices = new List<string>(new[] { "NDIS Wrapper", "NDIS", "Base", "PNP_TDI" });
			systemRegistryHive.AddServiceGroupsAfterSystemBusExtender(bootServices);
		}

		/// <summary>
		/// This will make sure that TCP/IP will still be marked as a boot service once windows installs it during GUI mode setup
		/// </summary>
		private void SetTCPIPSetupToBootStartForGUIMode()
		{
			if (!_installation.IsWindows2000) // Windows 2000 has an independent Tcpip service that does not require IPSec
				// Windows XP \ Server 2003: IPSec is required for TCPIP to work
				_installation.NetTCPIPInf.SetIPSecToBootStart();
			_installation.NetTCPIPInf.SetTCPIPToBootStart();

			// By default, QoS is not installed with Windows 2000 \ Server 2003
			// Note: Even if we enable it for Windows 2003, installing it manually will break the iSCSI connection and will cause a BSOD.
			if (_installation.IsWindowsXP)
			{
				// 'General Packet Classifier' is required for 'Packet Scheduler' to work
				// 'Packet Scheduler' is a key function of quality of service (QoS), which is installed by default for Windows XP
				_installation.NetGPCInf.SetGPCToBootStart();
				_installation.NetPacketSchedulerAdapterInf.SetPacketSchedulerAdapterToBootStart();

				// Not sure about the role of netpschd.inf, note that it doesn't contain an AddService entry.
				//m_installation.NetPacketSchedulerInf.SetPacketSchedulerToBootStart();
			}
		}

		public void AssignIPAddressToNetDeviceServices(bool staticIP)
		{
			foreach (var netDeviceService in _netDeviceServices)
				AssignIPAddressToNetDeviceService(netDeviceService, staticIP);
		}

		private void AssignIPAddressToNetDeviceService(NetworkDeviceService netDeviceService, bool staticIP)
		{
			string ipAddress;
			string subnetMask;
			string defaultGateway;
			if (staticIP)
			{
				Console.WriteLine("Please select TCP/IP settings for '" + netDeviceService.DeviceDescription + "':");
				Console.WriteLine("* Pressing Enter will default to 192.168.1.50 / 255.255.255.0 / 192.168.1.1");
				ipAddress = ReadValidIPv4Address("IP Address", "192.168.1.50");
				subnetMask = ReadValidIPv4Address("Subnet Mask", "255.255.255.0");
				defaultGateway = ReadValidIPv4Address("Default Gateway", "192.168.1.1");
			}
			else
			{
				ipAddress = "0.0.0.0";
				subnetMask = "0.0.0.0";
				defaultGateway = string.Empty;
			}

			AssignIPAddressToNetDeviceService(netDeviceService, _installation.SetupRegistryHive, ipAddress, subnetMask, defaultGateway);
			AssignIPAddressToNetDeviceService(netDeviceService, _installation.HiveSystemInf, ipAddress, subnetMask, defaultGateway);
		}
		
		private static void AssignIPAddressToNetDeviceService(NetworkDeviceService netDeviceService, ISystemRegistryHive systemRegistryHive,  string ipAddress, string subnetMask, string defaultGateway)
		{
			var netCfgInstanceID = netDeviceService.NetCfgInstanceID;

			var adapterKeyName = @"Parameters\Adapters\" + netCfgInstanceID;
			var adapterIPConfig = @"Tcpip\Parameters\Interfaces\" + netCfgInstanceID;
			var interfaceKeyName = @"Parameters\Interfaces\" + netCfgInstanceID;

			// this is some kind of reference to where the actual TCP/IP configuration is located
			systemRegistryHive.SetServiceRegistryKey("Tcpip", adapterKeyName, "IpConfig", RegistryValueKind.MultiString, new[] { adapterIPConfig });

			// DefaultGateway is not necessary for most people, but can ease the installation for people with complex networks
			systemRegistryHive.SetServiceRegistryKey("Tcpip", interfaceKeyName, "DefaultGateway", RegistryValueKind.MultiString, new[] { defaultGateway });
			// Extracurricular note: it's possible to use more than one IP address, but you have to specify subnet mask for it as well.
			systemRegistryHive.SetServiceRegistryKey("Tcpip", interfaceKeyName, "IPAddress", RegistryValueKind.MultiString, new[] { ipAddress });
			systemRegistryHive.SetServiceRegistryKey("Tcpip", interfaceKeyName, "SubnetMask", RegistryValueKind.MultiString, new[] { subnetMask });

			// Note related to GUI mode:
			// We already bind the device class instance to NetCfgInstanceID, and that's all that's necessary for TCP/IP to work during text-mode.
			// However, TCP/IP must be bound to NetCfgInstanceID as well, TCP/IP will work in GUI mode without it, but setup will fail at T-39
			// with the following error: "setup was unable to initialize Network installation components. the specific error code is 2"
			// and in the subsequent screen: "LoadLibrary returned error 1114 (45a)" (related to netman.dll, netshell.dll)

			// The first component in one entry corresponds to the first component in the other entries:
			systemRegistryHive.SetServiceRegistryKey("Tcpip", "Linkage", "Bind", RegistryValueKind.MultiString, new[] { @"\DEVICE\" + netCfgInstanceID });
			systemRegistryHive.SetServiceRegistryKey("Tcpip", "Linkage", "Export", RegistryValueKind.MultiString, new[] { @"\DEVICE\TCPIP_" + netCfgInstanceID });
			// NetCfgInstanceID should be quoted, HiveSystemInf should take care of the use of quote characters (special character):
			systemRegistryHive.SetServiceRegistryKey("Tcpip", "Linkage", "Route", RegistryValueKind.MultiString, new[] { QuotedStringUtils.Quote(netCfgInstanceID) });
		}

		private static string ReadValidIPv4Address(string name, string defaultAddress)
		{
			var retry = 2;
			while (retry > 0)
			{
				Console.Write(name + ": ");
				var address = Console.ReadLine();
				if (address.Trim() == string.Empty)
				{
					address = defaultAddress;
					Console.WriteLine("Defaulting to " + defaultAddress);
					return address;
				}

				if (!ValidadeAddress(address))
				{
					Console.WriteLine("Invalid " + name + " detected. you have to use IPv4!");
					retry--;
				}
				else
					return address;
			}

			Console.WriteLine("Invalid " + name + " detected, aborting!");
			Program.Exit();

			return defaultAddress;
		}

		// We won't use IPAddress.Parse because it's a dependency we do not want or need
		private static bool ValidadeAddress(string address)
		{
			var components = address.Split('.');
			if (components.Length != 4)
				return false;

			var arg1 = Conversion.ToInt32(components[0], -1);
			var arg2 = Conversion.ToInt32(components[1], -1);
			var arg3 = Conversion.ToInt32(components[2], -1);
			var arg4 = Conversion.ToInt32(components[3], -1);
			return
				arg1 >= 0 &&
				arg2 >= 0 &&
				arg3 >= 0 &&
				arg4 >= 0 &&
				arg1 <= 255 &&
				arg2 <= 255 &&
				arg3 <= 255 &&
				arg4 <= 255;
		}
	}
}
