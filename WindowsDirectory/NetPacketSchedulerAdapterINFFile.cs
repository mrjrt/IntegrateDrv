using IntegrateDrv.BaseClasses;

namespace IntegrateDrv.WindowsDirectory
{
	/// <summary>
	/// Miniport adapter for packet scheduler
	/// </summary>
	public class NetPacketSchedulerAdapterINFFile : ServiceINIFile
	{
		public NetPacketSchedulerAdapterINFFile() : base("netpsa.inf")
		{
		}

		public void SetPacketSchedulerAdapterToBootStart()
		{
			SetServiceToBootStart("PSchedMP.AddService");
		}
	}
}
