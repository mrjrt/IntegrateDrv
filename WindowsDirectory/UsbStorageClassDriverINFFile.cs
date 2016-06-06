using IntegrateDrv.BaseClasses;

namespace IntegrateDrv.WindowsDirectory
{
	public class UsbStorageClassDriverINFFile : ServiceINIFile
	{
		public UsbStorageClassDriverINFFile() : base("usbstor.inf")
		{}

		public void SetUsbStorageClassDriverToBootStart()
		{
			SetServiceToBootStart("USBSTOR.AddService");
			SetServiceLoadOrderGroup("USBSTOR.AddService", "Boot Bus Extender");
		}
	}
}
