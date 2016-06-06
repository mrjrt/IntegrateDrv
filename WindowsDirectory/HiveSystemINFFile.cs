using System;
using System.Collections.Generic;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.Interfaces;
using IntegrateDrv.Utilities.Strings;
using Microsoft.Win32;

namespace IntegrateDrv.WindowsDirectory
{
	public class HiveSystemINFFile : HiveINIFile, ISystemRegistryHive
	{
		public HiveSystemINFFile() : base("hivesys.inf")
		{
		}

		/// <summary>
		/// Should return 'WinNt' or 'ServerNT' (AFAIK 'LanmanNT' is only set when a domain controller is being configured)
		/// </summary>
		public string GetWindowsProductType()
		{
			const string hive = "HKLM";
			const string subKeyName = @"SYSTEM\CurrentControlSet\Control\ProductOptions";
			const string valueName = "ProductType";
			var productType = GetRegistryValueData(hive, subKeyName, valueName);
			return productType;
		}

		public int GetWindowsServicePackVersion()
		{
			const string hive = "HKLM";
			const string subKeyName = @"SYSTEM\CurrentControlSet\Control\Windows";
			const string valueName = "CSDVersion";
			var csdVersionString = GetRegistryValueData(hive, subKeyName, valueName);
			// The values of csdVersion will be 0x100 for Service Pack 1, 0x200 for Service Pack 2, and so forth.
			return Convert.ToInt32(csdVersionString, 16) >> 8;
		}

		public void SetCurrentControlSetRegistryKey(string keyName, string subKeyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			if (subKeyName != string.Empty)
				keyName = keyName + @"\" + subKeyName;
			SetCurrentControlSetRegistryKey(keyName, valueName, valueKind, valueData);
		}

		public void SetCurrentControlSetRegistryKey(string keyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			SetRegistryKey(@"SYSTEM\CurrentControlSet", keyName, valueName, valueKind, valueData);
		}

		public void SetServiceRegistryKey(string serviceName, string subKeyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			var keyName = @"Services\" + serviceName;
			SetCurrentControlSetRegistryKey(keyName, subKeyName, valueName, valueKind, valueData);
		}

		private void SetRegistryKey(string keyName, string subKeyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			if (subKeyName != string.Empty)
				keyName += "\\" + subKeyName;
			SetRegistryKey(keyName, valueName, valueKind, valueData);
		}

		private void SetRegistryKey(string keyName, string valueName, RegistryValueKind valueKind, object valueData)
		{
			SetRegistryKeyInternal(keyName, valueName, valueKind, GetFormattedValueData(valueData, valueKind));
		}

		// Internal should be used only by methods that pass properly formatted valueData or methods that reads directry from .inf
		/// <param name="valueKind"></param>
		/// <param name="valueData">string input must be quoted</param>
		/// <param name="keyName"></param>
		/// <param name="valueName"></param>
		private void SetRegistryKeyInternal(string keyName, string valueName, RegistryValueKind valueKind, string valueData)
		{
			var valueTypeHexString = GetRegistryValueTypeHexString(valueKind);
			const string hive = "HKLM";
			
			string lineFound;
			var lineIndex = GetLineStartIndex("AddReg", hive, keyName, valueName, out lineFound);
			var line = string.Format("{0},\"{1}\",\"{2}\",{3},{4}", hive, keyName, valueName, valueTypeHexString, valueData);
			if (lineIndex == -1)
				AppendLineToSection("AddReg", line);
			else
				UpdateLine(lineIndex, line);
		}

		private List<string> GetServiceGroupOrderEntry()
		{
			const string hive = "HKLM";
			const string keyName = @"SYSTEM\CurrentControlSet\Control\ServiceGroupOrder";
			const string valueName = "List";
			var serviceGroupOrderStringList = GetRegistryValueData(hive, keyName, valueName);
			var serviceGroupOrderList = GetCommaSeparatedValues(serviceGroupOrderStringList);
			for (var index = 0; index < serviceGroupOrderList.Count; index++)
				serviceGroupOrderList[index] = Unquote(serviceGroupOrderList[index]);
			return serviceGroupOrderList;
		}

		private void SetServiceGroupOrderEntry(List<string> serviceGroupOrder)
		{
			const string hive = "HKLM";
			const string keyName = @"SYSTEM\CurrentControlSet\Control\ServiceGroupOrder";
			const string valueName = "List";

			var valueData = GetFormattedMultiString(serviceGroupOrder.ToArray());

			UpdateRegistryValueData(hive, keyName, valueName, valueData);
		}

		public void AddServiceGroupsAfterSystemBusExtender(List<string> serviceGroupNames)
		{
			var serviceGroupOrder = GetServiceGroupOrderEntry();

			// remove existing entry (because it may be in the wrong place)
			foreach (var serviceGroupName in serviceGroupNames)
			{
				var index = StringUtils.IndexOfCaseInsensitive(serviceGroupOrder, serviceGroupName);
				if (index != -1)
					serviceGroupOrder.RemoveAt(index);
			}

			// add entry
			if (serviceGroupOrder.Count > 3)
				serviceGroupOrder.InsertRange(3, serviceGroupNames);
			else
				Console.WriteLine("Critical warning: hivesys.inf has been tampered with");

			SetServiceGroupOrderEntry(serviceGroupOrder);
		}

		public void AddDeviceToCriticalDeviceDatabase(string hardwareID, string serviceName)
		{
			AddDeviceToCriticalDeviceDatabase(hardwareID, serviceName, string.Empty);
		}

		public void AddDeviceToCriticalDeviceDatabase(string hardwareID, string serviceName, string classGUID)
		{
			hardwareID = hardwareID.Replace(@"\", "#");
			hardwareID = hardwareID.ToLower();
			SetCurrentControlSetRegistryKey(
				@"Control\CriticalDeviceDatabase\" + hardwareID,
				"Service",
				RegistryValueKind.String,
				serviceName);
			if (classGUID != string.Empty)
				SetCurrentControlSetRegistryKey(
					@"Control\CriticalDeviceDatabase\" + hardwareID,
					"ClassGUID",
					RegistryValueKind.String,
					classGUID);
		}

		public string AllocateVirtualDeviceInstanceID(string deviceClassName)
		{
			var keyName = @"SYSTEM\CurrentControlSet\Enum\Root\" + deviceClassName;
			return AllocateNumericInstanceID(keyName);
		}

		private string AllocateNumericInstanceID(string keyName)
		{
			var instanceIDInt = 0;
			var instanceID = instanceIDInt.ToString("0000");
			while(ContainsKey("HKLM", keyName + @"\" + instanceID))
			{
				instanceIDInt++;
				instanceID = instanceIDInt.ToString("0000");
			}
			return instanceID;
		}
	}
}
