using System;
using System.Collections.Generic;
using IntegrateDrv.Utilities.Strings;

namespace IntegrateDrv.Utilities
{
	public static class CommandLineParser
	{
		private static int IndexOfArgumentSeparator(string str)
		{
			return IndexOfArgumentSeparator(str, 0);
		}

		private static int IndexOfArgumentSeparator(string str, int startIndex)
		{
			var index = QuotedStringUtils.IndexOfUnquotedChar(str, ' ', startIndex);
			if (index >= 0)
			{
				while (index + 1 < str.Length && str[index + 1] == ' ')
					index++;
			}
			return index;
		}

		/// <summary>
		/// The method ignore backspace as escape character,
		/// this way "C:\Driver\" I: are turned into two arguments instead of one.
		/// </summary>
		public static string[] GetCommandLineArgsIgnoreEscape()
		{
			var commandLine = Environment.CommandLine;
			var argsList = new List<string>();
			var startIndex = 0;
			var endIndex = IndexOfArgumentSeparator(commandLine);
			while (endIndex != -1)
			{
				var length = endIndex - startIndex;
				var nextArg = commandLine.Substring(startIndex, length).Trim();
				nextArg = QuotedStringUtils.Unquote(nextArg);
				argsList.Add(nextArg);
				startIndex = endIndex + 1;
				endIndex = IndexOfArgumentSeparator(commandLine, startIndex);
			}

			var lastArg = commandLine.Substring(startIndex).Trim();
			lastArg = QuotedStringUtils.Unquote(lastArg);
			if (lastArg != string.Empty)
				argsList.Add(lastArg);

			argsList.RemoveAt(0); // remove the executable name
			return argsList.ToArray();
		}
	}
}
