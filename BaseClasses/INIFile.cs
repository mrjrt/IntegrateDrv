using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using IntegrateDrv.Utilities.FileSystem;
using IntegrateDrv.Utilities.Strings;

namespace IntegrateDrv.BaseClasses
{
	// Note: both .sif files and .inf files conform to .ini file specifications
	public partial class INIFile
	{
		private List<string> _sectionNamesCache;
		private Dictionary<string, List<string>> _sectionCache = new Dictionary<string, List<string>>();

		private Encoding _encoding = Encoding.ASCII;
			// we have to remeber the original encoding of the file, we use ASCII for new files

		protected INIFile()
		{
			FileName = string.Empty;
			Text = string.Empty;
		}

		/// <summary>
		/// Note regarding packed files: this method should be supplied with the unpacked file name!
		/// If the file is called nettcpip.in_, fileName should be nettcpip.inf!
		/// (this is the name in the packed container that we are looking for)
		/// </summary>
		protected INIFile(string fileName)
		{
			Text = string.Empty;
			FileName = fileName;
		}

		// Localized Windows editions and some drivers use Unicode encoding.
		// The supported formats are UTF-16 little endian and UTF-16 Big endian, and possibly UTF-8.
		private static Encoding GetEncoding(ref byte[] fileBytes)
		{
			if (fileBytes.Length >= 3)
			{
				if (fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF)
					return Encoding.UTF8;
			}

			if (fileBytes.Length >= 2)
			{
				if (fileBytes[0] == 0xFF && fileBytes[1] == 0xFE)
					return Encoding.Unicode;

				if (fileBytes[0] == 0xFE && fileBytes[1] == 0xFF)
					return Encoding.BigEndianUnicode;
			}
			// Note: Some localized versions of Windows use latin characters.
			//
			// During initial setup, the OEM code page specified under txtsetup.sif [nls] section will be used,
			// later, the ANSI codepage specified in that section will be used.
			//
			// It doesn't really matter which code page we use here as long as it preserves the non-ASCII characters
			// (both 437, 850, 1252 and 28591 works fine).
			//
			// Note: The only one that Mono supports is 28591.
			return Encoding.GetEncoding(28591);
		}

		/// <summary>
		/// File encoding is Ascii
		/// </summary>
		private void Read(string filePath)
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
			_encoding = GetEncoding(ref bytes);
			var text = _encoding.GetString(bytes);
			text = text.Replace("\r\r", "\r");
				// fixes an issue with Windows 2000's hivesys.inf, String-reader will read \r\r as two lines, and this will screw-up broken lines
			Text = text;

			ClearCache();
		}

		/// <summary>
		/// File encoding is Ascii
		/// </summary>
		public void ReadFromDirectory(string directoryPath)
		{
			if (FileName == string.Empty)
				throw new Exception("ReadFileFromDirectory - class has not been initizalized with a file name");
			Read(directoryPath + FileName);
		}

		private void ClearCache()
		{
			_sectionNamesCache = null;
			_sectionCache = new Dictionary<string, List<string>>();
		}

		private void ClearCachedSection(string sectionName)
		{
			// m_sectionCache stores sectionName in lowercase
			_sectionCache.Remove(sectionName.ToLower());
		}

		public List<string> GetSection(string sectionName)
		{
			// m_sectionCache stores sectionName in lowercase
			var sectionCacheKey = sectionName.ToLower();
			if (_sectionCache.ContainsKey(sectionCacheKey))
				return _sectionCache[sectionCacheKey];
			var section = GetSectionInText(sectionName, Text);
			_sectionCache.Add(sectionCacheKey, section);
			return section;
		}

		public List<string> GetValuesOfKeyInSection(string sectionName, string key)
		{
			var section = GetSection(sectionName);
			foreach (var line in section)
			{
				var keyAndValues = GetKeyAndValues(line);
				if (keyAndValues.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase))
					return keyAndValues.Value;
			}
			return new List<string>();
		}

		public List<string> SectionNames
		{
			get { return _sectionNamesCache ?? (_sectionNamesCache = ListSections(Text)); }
		}

		// Note: it is valid to have an empty section (e.g. [files.none])
		protected void AddSection(string sectionName)
		{
			// Add an empty line before section header
			AppendLine(string.Empty);
			AppendLine("[" + sectionName + "]");
			if (_sectionNamesCache != null)
				_sectionNamesCache.Add(sectionName);
		}

		protected void AppendLineToSection(string sectionName, string lineToAppend)
		{
			var writer = new StringWriter();
			var reader = new StringReader(Text);
			var index = 0;

			var sectionHeaderLastIndex = GetLastIndexOfSectionHeader(sectionName);
			// we insert one line after the last non-empty line, search start from one line after the section header
			var insertIndex = GetLastIndexOfNonEmptyLineInSection(sectionHeaderLastIndex + 1) + 1;
			var done = false;
			var line = reader.ReadLine();
			while (line != null)
			{
				if (index == insertIndex)
				{
					writer.WriteLine(lineToAppend);
					done = true;
				}
				writer.WriteLine(line);

				line = reader.ReadLine();
				index++;
			}
			if (done == false)
				writer.WriteLine(lineToAppend);
			Text = writer.ToString();

			IsModified = true;
			ClearCachedSection(sectionName);
		}

		protected void InsertLine(int lineIndex, string lineToInsert)
		{
			var writer = new StringWriter();
			var reader = new StringReader(Text);
			var index = 0;

			var line = reader.ReadLine();
			while (line != null)
			{
				if (index == lineIndex)
					writer.WriteLine(lineToInsert);
				writer.WriteLine(line);

				line = reader.ReadLine();
				index++;
			}
			Text = writer.ToString();

			IsModified = true;
			ClearCache();
		}

		protected void AppendLine(string lineToAppend)
		{
			// Windows 2000: txtsetup.sif usually ends with an EOF marker followed by "\r\n".
			// Windows XP x86: txtsetup.sif usually ends with an EOF marker.
			// Note: In both cases, the EOF marker is not required.
			// Note: If an EOF is present, lines following it will be ignored.
			const char eof = (char) 0x1A;
			if (Text.EndsWith(eof.ToString(CultureInfo.InvariantCulture)))
				Text = Text.Remove(Text.Length - 1);
			else if (Text.EndsWith(eof + "\r\n"))
				Text = Text.Remove(Text.Length - 3);

			if (!Text.EndsWith("\r\n"))
				Text += "\r\n";
			Text += lineToAppend + "\r\n";

			IsModified = true;
			ClearCache();
		}

		protected void DeleteLine(int lineIndex)
		{
			DeleteLine(lineIndex, true);
		}

		private void DeleteLine(int lineIndex, bool removeTrailingBrokenLines)
		{
			UpdateLine(lineIndex, null, removeTrailingBrokenLines);
		}

		protected void UpdateLine(int lineIndex, string updatedLine)
		{
			UpdateLine(lineIndex, updatedLine, false);
		}

		protected void UpdateLine(int lineIndex, string updatedLine, bool removeTrailingBrokenLines)
		{
			var writer = new StringWriter();
			var reader = new StringReader(Text);
			var index = 0;

			var line = reader.ReadLine();
			while (line != null)
			{
				if (index == lineIndex)
				{
					if (updatedLine != null)
						writer.WriteLine(updatedLine);
					if (removeTrailingBrokenLines)
					{
						while (line.EndsWith(@"\"))
							line = reader.ReadLine();
					}
				}
				else
					writer.WriteLine(line);

				line = reader.ReadLine();
				index++;
			}
			Text = writer.ToString();

			IsModified = true;
			ClearCache();
		}

		/// <summary>
		/// The same section can appear multiple times in a single file
		/// </summary>
		private int GetLastIndexOfSectionHeader(string sectionName)
		{
			var index = 0;
			var lastIndex = -1;
			var reader = new StringReader(Text);
			var sectionHeader = string.Format("[{0}]", sectionName);

			var line = reader.ReadLine();
			while (line != null)
			{
				if (line.TrimStart(' ').StartsWith(sectionHeader, StringComparison.InvariantCultureIgnoreCase))
					lastIndex = index;

				line = reader.ReadLine();
				index++;
			}

			return lastIndex;
		}

		private int GetLastIndexOfNonEmptyLineInSection(int startIndex)
		{
			var index = 0;
			var lastIndex = startIndex - 1;
			var reader = new StringReader(Text);
			var line = reader.ReadLine();
			while (line != null)
			{
				if (index >= startIndex)
				{
					if (IsSectionHeader(line))
						return lastIndex;

					if (line.Trim() != string.Empty)
						lastIndex = index;
				}
				line = reader.ReadLine();
				index++;
			}

			if (lastIndex == -1)
				lastIndex = index;
			return lastIndex;
		}

		public void SaveToDirectory(string directory)
		{
			Save(directory + FileName);
		}

		private void Save(string path)
		{
			// if an existing file was read, m_text will contain the BOM character, otherwise we write ASCII and there is no need for BOM.
			var bytes = _encoding.GetBytes(Text);
			FileSystemUtils.ClearReadOnlyAttribute(path);
			FileSystemUtils.WriteFile(path, bytes);
			IsModified = false;
		}

		private static List<string> GetSectionInText(string sectionName, string text)
		{
			var result = new List<string>();
			var reader = new StringReader(text);
			var sectionHeader = string.Format("[{0}]", sectionName);
			var outsideSection = true;
			var line = reader.ReadLine();
			while (line != null)
			{
				if (outsideSection)
				{
					if (line.TrimStart(' ').StartsWith(sectionHeader, StringComparison.InvariantCultureIgnoreCase))
						outsideSection = false;
				}
				else
				{
					if (IsSectionHeader(line))
					{
						// section ended, but the same section can appear multiple times inside a single file
						outsideSection = true;
						continue;
					}

					if (!IsComment(line) && line.Trim() != string.Empty)
						result.Add(line);
				}

				line = reader.ReadLine();
			}
			return result;
		}

		private static List<string> ListSections(string text)
		{
			var result = new List<string>();
			var reader = new StringReader(text);
			var line = reader.ReadLine();
			while (line != null)
			{
				if (IsSectionHeader(line))
				{
					var sectionNameStart = line.IndexOf('[') + 1;
					var sectionNameEnd = line.IndexOf(']', sectionNameStart + 1) - 1;
					if (sectionNameStart >= 0 && sectionNameEnd > sectionNameStart)
					{
						var sectionName = line.Substring(sectionNameStart, sectionNameEnd - sectionNameStart + 1);
						// the same section can appear multiple times inside a single file
						if (!StringUtils.ContainsCaseInsensitive(result, sectionName))
							result.Add(sectionName);
					}
				}
				line = reader.ReadLine();

			}
			return result;
		}

		protected static string GetKey(string line)
		{
			return GetKeyAndValues(line).Key;
		}

		public static KeyValuePair<string, List<string>> GetKeyAndValues(string line)
		{
			var index = line.IndexOf("=", StringComparison.Ordinal);
			if (index >= 0)
			{
				var key = line.Substring(0, index).Trim();
				var value = line.Substring(index + 1);
				var values = GetCommaSeparatedValues(value);

				return new KeyValuePair<string, List<string>>(key, values);
			}

			return new KeyValuePair<string, List<string>>(line, new List<string>());
		}

		public static List<string> GetCommaSeparatedValues(string value)
		{
			var commentIndex = QuotedStringUtils.IndexOfUnquotedChar(value, ';');
			if (commentIndex >= 0)
				value = value.Substring(0, commentIndex);
			var values = QuotedStringUtils.SplitIgnoreQuotedSeparators(value, ',');
			for (var index = 0; index < values.Count; index++)
				values[index] = values[index].Trim();
			return values;
		}

		protected int GetLineIndex(string sectionName, string lineToFind)
		{
			Predicate<string> lineEquals =
				delegate(string line) { return line.Equals(lineToFind, StringComparison.InvariantCultureIgnoreCase); };
			string lineFound;
			return GetLineIndex(sectionName, lineEquals, out lineFound);
		}

		protected int GetLineIndexByKey(string sectionName, string key)
		{
			string lineFound;
			return GetLineIndexByKey(sectionName, key, out lineFound);
		}

		protected int GetLineIndexByKey(string sectionName, string key, out string lineFound)
		{
			Predicate<string> lineKeyMatch =
				delegate(string line) { return GetKey(line).Equals(key, StringComparison.InvariantCultureIgnoreCase); };
			return GetLineIndex(sectionName, lineKeyMatch, out lineFound);
		}

		protected int GetLineIndex(string sectionName, Predicate<string> lineFilter, out string lineFound)
		{
			return GetLineIndex(sectionName, lineFilter, out lineFound, false);
		}

		/// <summary>
		/// Will return the index of the first line (lineIndex, line) in the section for which the predicate will return true
		/// </summary>
		protected int GetLineIndex(string sectionName, Predicate<string> lineFilter, out string lineFound,
			bool appendBrokenLines)
		{
			var reader = new StringReader(Text);
			var sectionHeader = string.Format("[{0}]", sectionName);
			var outsideSection = true;
			var line = reader.ReadLine();
			var index = 0;
			while (line != null)
			{
				if (outsideSection)
				{
					if (line.TrimStart(' ').StartsWith(sectionHeader, StringComparison.InvariantCultureIgnoreCase))
						// section header could have a comment following
						outsideSection = false;
				}
				else
				{
					if (IsSectionHeader(line))
					{
						// section ended, but the same section can appear multiple times inside a single file
						outsideSection = true;
						continue;
					}

					if (lineFilter(line))
					{
						lineFound = line;
						if (appendBrokenLines)
						{
							while (line.EndsWith(@"\")) // value data will continuer in next line 
							{
								lineFound = lineFound.Substring(0, lineFound.Length - 1); // remove trailing slash
								line = reader.ReadLine();
								lineFound = lineFound + line.TrimStart(' ');
							}
						}
						return index;
					}
				}

				line = reader.ReadLine();
				index++;
			}
			lineFound = string.Empty;
			return -1;
		}

		private static bool IsSectionHeader(string line)
		{
			return line.TrimStart(' ').StartsWith("[");
		}

		private static bool IsComment(string line)
		{
			return (line.TrimStart(' ').StartsWith(";") || line.TrimStart(' ').StartsWith("#"));
		}

		protected static string Quote(string str)
		{
			return QuotedStringUtils.Quote(str);
		}

		public static string Unquote(string str)
		{
			return QuotedStringUtils.Unquote(str);
		}

		public string FileName { get; private set; }

		protected string Text { private get; set; }

		public bool IsModified { get; protected set; }

		public static string TryGetValue(List<string> values, int valueIndex)
		{
			var result = string.Empty;
			if (values.Count > valueIndex)
				result = values[valueIndex];
			return result;
		}
	}
}