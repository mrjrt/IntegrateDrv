using System;
using System.Collections.Generic;

namespace IntegrateDrv.ExportedRegistry
{
	public class ExportedRegistryKey
	{
		private readonly string _keyName = string.Empty; // full key name
		private readonly ExportedRegistryINI _registry;

		public ExportedRegistryKey(ExportedRegistryINI registry, string keyName)
		{
			_registry = registry;
			_keyName = keyName;
		}

		public ExportedRegistryKey OpenSubKey(string subKeyName)
		{
			return new ExportedRegistryKey(_registry, _keyName + @"\" + subKeyName);
		}

		public object GetValue(string name, object defaultValue)
		{
			var result = _registry.GetValue(_keyName, name) ?? defaultValue;
			return result;
		}

		public IEnumerable<string> GetSubKeyNames()
		{
			var result = new List<string>();
			foreach (var sectionName in _registry.SectionNames)
			{
				if (sectionName.StartsWith(_keyName + @"\", StringComparison.InvariantCultureIgnoreCase))
				{
					var subKeyName = sectionName.Substring(_keyName.Length + 1).Split('\\')[0];
					if (!result.Contains(subKeyName))
						result.Add(subKeyName);
				}
			}
			return result.ToArray();
		}

		/// <summary>
		/// Full key name
		/// </summary>
		public string Name
		{
			get { return _keyName; }
		}
	}
}
