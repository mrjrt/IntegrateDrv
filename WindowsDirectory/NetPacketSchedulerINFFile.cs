using IntegrateDrv.BaseClasses;

namespace IntegrateDrv.WindowsDirectory
{
	public class NetPacketSchedulerINFFile : ServiceINIFile
	{
		public NetPacketSchedulerINFFile() : base("netpschd.inf")
		{ 
		}

		public void SetPacketSchedulerToBootStart()
		{
			SetServiceToBootStart("PSched.AddService");
		}
	}
}
