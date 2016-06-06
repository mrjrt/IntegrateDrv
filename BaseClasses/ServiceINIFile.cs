using System;
using IntegrateDrv.Utilities.Conversion;

namespace IntegrateDrv.BaseClasses
{
	public class ServiceINIFile : INIFile
	{
		protected ServiceINIFile(string fileName) : base(fileName)
		{ }

		protected void SetServiceToBootStart(string serviceInstallSectionName)
		{
			string line;
			var lineIndex = GetLineIndexByKey(serviceInstallSectionName, "StartType", out line);
			var valueStartIndex = line.IndexOf("=", StringComparison.Ordinal) + 1;
			var startTypeStr = GetCommaSeparatedValues(line.Substring(valueStartIndex))[0].Trim();
			if (startTypeStr.StartsWith("0x"))
			{
				startTypeStr = startTypeStr.Substring(2);
			}
			var startType = Conversion.ToInt32(startTypeStr, -1);
			if (startType != 0) // do not modify .inf that already has StartType set to 0, as it might break its digital signature unnecessarily.
			{
				line = line.Substring(0, valueStartIndex) + " 0 ;SERVICE_BOOT_START";
				UpdateLine(lineIndex, line);
			}
		}

		protected void SetServiceLoadOrderGroup(string serviceInstallSectionName, string loadOrderGroup)
		{
			string line;
			var lineIndex = GetLineIndexByKey(serviceInstallSectionName, "LoadOrderGroup", out line);
			if (lineIndex >= 0)
			{
				var valueStartIndex = line.IndexOf("=", StringComparison.Ordinal) + 1;
				var existingLoadOrderGroup = GetCommaSeparatedValues(line.Substring(valueStartIndex))[0].Trim();
				if (!string.Equals(loadOrderGroup, existingLoadOrderGroup, StringComparison.InvariantCultureIgnoreCase)) // do not modify .inf that already has StartType set to 0, as it might break its digital signature unnecessarily.
				{
					line = line.Substring(0, valueStartIndex) + " " + loadOrderGroup;
					UpdateLine(lineIndex, line);
				}
			
			}
			else
			{
				line = "LoadOrderGroup = " + loadOrderGroup;
				AppendLineToSection(serviceInstallSectionName, line);
			}
		}
	}
}
