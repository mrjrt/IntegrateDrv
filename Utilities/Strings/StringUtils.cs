using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IntegrateDrv.Utilities.Strings
{
	public static class StringUtils
	{
		public static List<string> Split(string text, char separator)
		{
			var result = new List<string>(text.Split(separator));
			return result;
		}

		public static string Join(List<string> parts)
		{
			return Join(parts, string.Empty);
		}

		public static string Join(List<string> parts, string separator)
		{
			var sBuilder = new StringBuilder();
			for (var index = 0; index < parts.Count; index++)
			{
				if (index != 0)
					sBuilder.Append(separator);
				sBuilder.Append(parts[index]);
			}
			return sBuilder.ToString();
		}

		public static bool ContainsCaseInsensitive(List<string> list, string value)
		{
			return (IndexOfCaseInsensitive(list, value) != -1);
		}

		public static int IndexOfCaseInsensitive(List<string> list, string value)
		{
			for(var index = 0; index < list.Count; index++)
			{
				if (list[index].Equals(value, StringComparison.InvariantCultureIgnoreCase))
					return index;
			}
			return -1;
		}

		public static string ContainsRegex(IEnumerable<string> list, string value)
		{
			return ContainsRegex(list, value, RegexOptions.None);
		}

		public static string ContainsRegex(IEnumerable<string> list, string value, RegexOptions regexOptions)
		{
			value = string.Concat('^', value, '$');
			foreach (var listValue in list)
			{
				var result = Regex.Match(listValue, value, regexOptions);
				//if(Regex.IsMatch(listValue, value, regexOptions))
				if(result.Success)
					return result.Value;
			}

			return null;
		}
	}
}
