using System.Collections.Generic;
using Microsoft.Win32;

namespace IntegrateDrv.Interfaces
{
	public interface ISystemRegistryHive
	{
		void SetCurrentControlSetRegistryKey(string keyName, string subKeyName, string valueName, RegistryValueKind valueKind, object valueData);
		void SetCurrentControlSetRegistryKey(string keyName, string valueName, RegistryValueKind valueKind, object valueData);
		void SetServiceRegistryKey(string serviceName, string subKeyName, string valueName, RegistryValueKind valueKind, object valueData);
		void AddServiceGroupsAfterSystemBusExtender(List<string> serviceGroupNames);
	}
}
