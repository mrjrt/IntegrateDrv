using System.Collections.Generic;
using System.Globalization;

namespace IntegrateDrv.Utilities.Strings
{
	public static class QuotedStringUtils
	{
		public static string Quote(string str)
		{
			return string.Format("\"{0}\"", str);
		}

		public static string Unquote(string str)
		{
			var quote = '"'.ToString(CultureInfo.InvariantCulture);
			if (str.Length >= 2 && str.StartsWith(quote) && str.EndsWith(quote))
				return str.Substring(1, str.Length - 2);
			return str;
		}

		public static int IndexOfUnquotedChar(string str, char charToFind)
		{
			return IndexOfUnquotedChar(str, charToFind, 0);
		}

		public static int IndexOfUnquotedChar(string str, char charToFind, int startIndex)
		{
			if (startIndex >= str.Length)
				return -1;

			var inQuote = false;
			var index = startIndex;
			while (index < str.Length)
			{
				if (str[index] == '"')
					inQuote = !inQuote;
				else if (!inQuote && str[index] == charToFind)
					return index;
				index++;
			}
			return -1;
		}

		public static List<string> SplitIgnoreQuotedSeparators(string str, char separator)
		{
			var result = new List<string>();
			var nextEntryIndex = 0;
			var separatorIndex = IndexOfUnquotedChar(str, separator);
			while (separatorIndex >= nextEntryIndex)
			{
				result.Add(str.Substring(nextEntryIndex, separatorIndex - nextEntryIndex));

				nextEntryIndex = separatorIndex + 1;
				separatorIndex = IndexOfUnquotedChar(str, separator, nextEntryIndex);
			}
			result.Add(str.Substring(nextEntryIndex));
			return result;
		}
	}
}
