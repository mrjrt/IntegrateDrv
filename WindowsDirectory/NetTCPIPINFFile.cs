using IntegrateDrv.BaseClasses;

namespace IntegrateDrv.WindowsDirectory
{
	public class NetTCPIPINFFile : ServiceINIFile
	{
		public NetTCPIPINFFile() : base("nettcpip.inf")
		{
		}

		public void SetTCPIPToBootStart()
		{
			SetServiceToBootStart("Install.AddService.TCPIP");
		}

		public void SetIPSecToBootStart()
		{
			SetServiceToBootStart("Install.AddService.IPSEC");
		}
	}
}
