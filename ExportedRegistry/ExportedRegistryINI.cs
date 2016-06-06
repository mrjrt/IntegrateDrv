using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.Utilities.FileSystem;
using Microsoft.Win32;

namespace IntegrateDrv.ExportedRegistry
{
	public class ExportedRegistryINI : INIFile
	{
		public ExportedRegistryINI(string filePath)
		{
			Text = ReadUnicode(filePath);
		}

		public ExportedRegistryKey LocalMachine
		{
			get { return new ExportedRegistryKey(this, "HKEY_LOCAL_MACHINE"); }
		}

		public object GetValue(string keyName, string valueName)
		{
			var lineStart = string.Format("\"{0}\"=", valueName);
			string lineFound;
			var lineIndex = GetLineStartIndex(keyName, lineStart, out lineFound);
			if (lineIndex >= 0)
			{
				var valueStr = lineFound.Substring(lineStart.Length);
				var result = ParseValueDataString(valueStr);
				return result;
			}

			return null;
		}

		private int GetLineStartIndex(string sectionName, string lineStart, out string lineFound)
		{
			Predicate<string> lineStartsWith =
				line =>
					line.TrimStart(' ')
					.StartsWith(lineStart, StringComparison.InvariantCultureIgnoreCase);
			return GetLineIndex(sectionName, lineStartsWith, out lineFound, true);
		}

		private static object ParseValueDataString(string valueData)
		{
			RegistryValueKind valueKind;
			return ParseValueDataString(valueData, out valueKind);
		}

		private static object ParseValueDataString(string valueData, out RegistryValueKind valueKind)
		{
			if (valueData.StartsWith("dword:"))
			{
				valueKind = RegistryValueKind.DWord;
				valueData = valueData.Substring(6);
				try
				{
					return Convert.ToInt32(valueData);
				}
				catch
				{
					return null;
				}
			}

			if (valueData.StartsWith("hex:"))
			{
				valueKind = RegistryValueKind.Binary;
				valueData = valueData.Substring(4);
				return ParseByteValueDataString(valueData);
			}

			if (valueData.StartsWith("hex(7):"))
			{
				valueKind = RegistryValueKind.MultiString;
				valueData = valueData.Substring(7);
				var bytes = ParseByteValueDataString(valueData);
				var str = Encoding.Unicode.GetString(bytes);
				return str.Split('\0');
			}

			if (valueData.StartsWith("hex(2):"))
			{
				valueKind = RegistryValueKind.ExpandString;
				valueData = valueData.Substring(7);
				var bytes = ParseByteValueDataString(valueData);
				return Encoding.Unicode.GetString(bytes);
			}

			if (valueData.StartsWith("hex(b):"))
			{
				// little endian
				valueKind = RegistryValueKind.QWord;
				valueData = valueData.Substring(7);
				var bytes = ParseByteValueDataString(valueData);
				return BitConverter.ToUInt64(bytes, 0);
			}

			if (valueData.StartsWith("hex(4):"))
			{
				// little endian
				valueKind = RegistryValueKind.DWord;
				valueData = valueData.Substring(7);
				var bytes = ParseByteValueDataString(valueData);
				return BitConverter.ToUInt32(bytes, 0);
			}

			if (valueData.StartsWith("hex(5):"))
			{
				// big endian
				valueKind = RegistryValueKind.DWord;
				valueData = valueData.Substring(7);
				var bytes = ParseByteValueDataString(valueData);
				var reversedBytes = new byte[4];
				for (var index = 0; index < 4; index++)
					reversedBytes[index] = bytes[3 - index];

				return BitConverter.ToUInt32(reversedBytes, 0);
			}

			if (valueData.StartsWith("hex(0):"))
			{
				valueKind = RegistryValueKind.Unknown;
				return new byte[0];
			}

			valueKind = RegistryValueKind.String;
			return Unquote(valueData);
		}

		private static byte[] ParseByteValueDataString(string valueData)
		{
			var byteStringList = GetCommaSeparatedValues(valueData);
			var byteList = new List<byte>();
			for (var index = 0; index < byteStringList.Count; index++)
			{
				var data = Convert.ToByte(byteStringList[index], 16); // byte values are in Hex
				byteList.Add(data);
			}
			return byteList.ToArray();
		}

		private static string ReadUnicode(string filePath)
		{
			var bytes = new byte[0];
			try
			{
				bytes = FileSystemUtils.ReadFile(filePath);
			}
			catch (IOException)
			{
				// usually it means the device is not ready (disconnected network drive / CD-ROM)
				Console.WriteLine("Error: IOException, Could not read file: " + filePath);
				Program.Exit();
			}
			catch (UnauthorizedAccessException)
			{
				Console.WriteLine("Error: Access Denied, Could not read file: " + filePath);
				Program.Exit();
			}
			catch
			{
				Console.WriteLine("Error: Could not read file: " + filePath);
				Program.Exit();
			}
			var result = Encoding.Unicode.GetString(bytes);
			return result;
		}
	}
}