using System;
using System.Globalization;
using IntegrateDrv.BaseClasses;
using IntegrateDrv.Utilities.Conversion;
using IntegrateDrv.Utilities.Strings;

namespace IntegrateDrv.WindowsDirectory
{
	public partial class TextSetupINFFile : INIFile
	{
		public TextSetupINFFile() : base("txtsetup.sif")
		{
		}

		private bool IsSourceDiskIDTaken(string architectureIdentifier, string sourceDiskID)
		{
			var section = GetSection(string.Format("SourceDisksNames.{0}", architectureIdentifier));
			foreach(var line in section)
			{
				var key = GetKey(line);
				if (key == sourceDiskID)
					return true;
			}
			return false;
		}

		private bool IsWinntDirectoryIDTaken(string winntDirectoryID)
		{
			var section = GetSection("WinntDirectories");
			foreach(var line in section)
			{
				var key = GetKey(line);
				if (key == winntDirectoryID)
					return true;
			}
			return false;
		}

		/// <returns>-1 if entry was not found</returns>
		private int GetSourceDiskID(string architectureIdentifier, string sourceDirectoryPathInMediaRootForm)
		{
			var section = GetSection(string.Format("SourceDisksNames.{0}", architectureIdentifier));
			foreach (var line in section)
			{ 
				var keyAndValues = GetKeyAndValues(line);
				var path = TryGetValue(keyAndValues.Value, 3);
				if (sourceDirectoryPathInMediaRootForm.Equals(path, StringComparison.InvariantCultureIgnoreCase))
					return Convert.ToInt32(keyAndValues.Key);
			}
			return -1;
		}

		public int AllocateSourceDiskID(string architectureIdentifier, string sourceDirectoryPathInMediaRootForm)
		{
			var sourceDiskID = GetSourceDiskID(architectureIdentifier, sourceDirectoryPathInMediaRootForm);
			if (sourceDiskID == -1)
			{
				sourceDiskID = 2001;
				while (IsSourceDiskIDTaken(architectureIdentifier.ToLower(), sourceDiskID.ToString(CultureInfo.InvariantCulture)))
					sourceDiskID++;

				AddSourceDiskEntry(architectureIdentifier, sourceDiskID.ToString(CultureInfo.InvariantCulture), sourceDirectoryPathInMediaRootForm);
			}
			return sourceDiskID;
		}

		private void AddSourceDiskEntry(string architectureIdentifier, string sourceDiskID, string sourceDirectoryPathInMediaRootForm)
		{
			var sectionName = string.Format("SourceDisksNames.{0}", architectureIdentifier);
			var values = GetValuesOfKeyInSection(sectionName, "1");

			// use the values from the first entry. it's not straight forward to determine the cdtagfile for Windows 2000 (each edition has a different tagfile)
			var cdname = TryGetValue(values, 0);
			var cdtagfile = TryGetValue(values, 1);

			var line = string.Format("{0} = {1},{2},,{3}", sourceDiskID, cdname, cdtagfile, sourceDirectoryPathInMediaRootForm);
			AppendLineToSection(sectionName, line);
		}

		private int GetWinntDirectoryID(string winntDirectoryPath)
		{
			var section = GetSection("WinntDirectories");
			foreach (var line in section)
			{
				var keyAndValues = GetKeyAndValues(line);
				var path = keyAndValues.Value[0];
				if (winntDirectoryPath.Equals(path, StringComparison.InvariantCultureIgnoreCase))
					return Convert.ToInt32(keyAndValues.Key);
			}
			return -1;
		}

		public int AllocateWinntDirectoryID(string winntDirectoryPath)
		{
			var winntDirectoryID = GetWinntDirectoryID(winntDirectoryPath);
			if (winntDirectoryID == -1)
			{
				winntDirectoryID = 3001;
				while (IsWinntDirectoryIDTaken(winntDirectoryID.ToString(CultureInfo.InvariantCulture)))
					winntDirectoryID++;

				AddWinntDirectory(winntDirectoryID.ToString(CultureInfo.InvariantCulture), winntDirectoryPath);
			}
			return winntDirectoryID;
		}

		private void AddWinntDirectory(string winntDirectoryID, string winntDirectoryPath)
		{
			var line = string.Format("{0} = {1}", winntDirectoryID, winntDirectoryPath);
			AppendLineToSection("WinntDirectories", line);
		}

		public void SetSourceDisksFileDriverEntry(string architectureIdentifier, string fileName, FileCopyDisposition fileCopyDisposition)
		{
			SetSourceDisksFileDriverEntry(architectureIdentifier, fileName, fileCopyDisposition, string.Empty);
		}

		public void SetSourceDisksFileDriverEntry(string architectureIdentifier, string fileName, FileCopyDisposition fileCopyDisposition, string destinationFileName)
		{
			// SourceDisksNames 1 = Setup Directory (e.g. \I386)
			SetSourceDisksFileDriverEntry(1, architectureIdentifier, fileName, fileCopyDisposition, destinationFileName);
		}

		private void SetSourceDisksFileDriverEntry(int sourceDiskID, string architectureIdentifier, string fileName, FileCopyDisposition fileCopyDisposition, string destinationFileName)
		{
			var sectionName = string.Format("SourceDisksFiles.{0}", architectureIdentifier);
			// first value is the sourceDiskID
			var lineIndex = GetLineIndexByFileNameAndWinnntDirectoryID(sectionName, fileName, (int)WinntDirectoryName.System32_Drivers);
			var newLine = GetSourceDisksFileDriverEntry(sourceDiskID, fileName, fileCopyDisposition, destinationFileName, MinorOSVersion);
			if (lineIndex == -1)
				AppendLineToSection(sectionName, newLine);
			else
				UpdateLine(lineIndex, newLine);
		}

		public void SetSourceDisksFileDllEntry(string architectureIdentifier, string fileName)
		{
			var sectionName = string.Format("SourceDisksFiles.{0}", architectureIdentifier);
			// first value is the sourceDiskID
			var lineIndex = GetLineIndexByFileNameAndWinnntDirectoryID(sectionName, fileName, (int)WinntDirectoryName.System32);
			var newLine = GetSourceDisksFileDllEntry(fileName, MinorOSVersion);
			if (lineIndex == -1)
				AppendLineToSection(sectionName, newLine);
			else
				UpdateLine(lineIndex, newLine);
		}

		public void SetSourceDisksFileEntry(string architectureIdentifier, int sourceDiskID, int destinationWinntDirectoryID, string fileName, FileCopyDisposition fileCopyDisposition)
		{
			var sectionName = string.Format("SourceDisksFiles.{0}", architectureIdentifier);
			// first value is the sourceDiskID
			var lineIndex = GetLineIndexByFileNameAndWinnntDirectoryID(sectionName, fileName, destinationWinntDirectoryID);
			var line = GetSourceDisksFileEntry(sourceDiskID, destinationWinntDirectoryID, fileName, false, fileCopyDisposition, fileCopyDisposition, string.Empty, MinorOSVersion);

			if (lineIndex == -1)
				AppendLineToSection(sectionName, line);
			else
				UpdateLine(lineIndex, line);
		}

		private int GetLineIndexByFileNameAndWinnntDirectoryID(string sourceDiskFilesSectionName, string fileName, int winntDirectoryID)
		{
			Predicate<string> lineMatch = delegate(string line) { return GetKey(line).Equals(fileName, StringComparison.InvariantCultureIgnoreCase) && GetKeyAndValues(line).Value[7].Equals(winntDirectoryID.ToString(CultureInfo.InvariantCulture), StringComparison.InvariantCultureIgnoreCase); };
			string lineFound;
			return GetLineIndex(sourceDiskFilesSectionName, lineMatch, out lineFound);
		}

		public void AddDeviceToCriticalDeviceDatabase(string hardwareID, string serviceName)
		{
			AddDeviceToCriticalDeviceDatabase(hardwareID, serviceName, string.Empty);
		}

		public void AddDeviceToCriticalDeviceDatabase(string hardwareID, string serviceName, string classGUID)
		{
			var line = string.Format("{0} = {1}", hardwareID, Quote(serviceName));
			if (classGUID != string.Empty)
				line += "," + classGUID;

			var lineIndex = GetLineIndexByKey("HardwareIdsDatabase", hardwareID);
			if (lineIndex == -1)
				AppendLineToSection("HardwareIdsDatabase", line);
			else
				UpdateLine(lineIndex, line);
		}

		public void RemoveDeviceFromCriticalDeviceDatabase(string hardwareID)
		{
			string lineFound;
			var lineIndex = GetLineIndexByKey("HardwareIdsDatabase", hardwareID, out lineFound);
			if (lineIndex >= 0)
				// Comment out this CDDB entry
				UpdateLine(lineIndex, ";" + lineFound);
		}

		/// <summary>
		/// The section will be created if it does not exist
		/// </summary>
		public void AddFileToFilesSection(string sectionName, string fileName, int winntDirectoryID)
		{
			if (!SectionNames.Contains(sectionName))
				AddSection(sectionName);

			// Note: the key here is a combination of filename and directory,
			// so the same file could be copied to two directories.
			var entry = string.Format("{0},{1}", fileName, winntDirectoryID);
			var lineIndex = GetLineIndexByKey(sectionName, entry);
			if (lineIndex == -1)
				AppendLineToSection(sectionName, entry);
		}

		// Apparently this tells setup not to try to copy during GUI phase files that were already
		// copied and deleted from source during text mode, and will prevent copy error in some cases:
		// http://www.msfn.org/board/topic/94894-fileflags-section-of-txtsetupsif/
		// http://www.ryanvm.net/forum/viewtopic.php?t=1653
		// http://www.wincert.net/forum/index.php?/topic/1933-addon-genuine-advantage/
		public void SetFileFlagsEntryForDriver(string filename)
		{
			var line = string.Format("{0} = 16", filename);
			var lineIndex = GetLineIndex("FileFlags", line);
			if (lineIndex == -1)
				AppendLineToSection("FileFlags", line);
		}

		[Obsolete]
		public void UseMultiprocessorHal()
		{
			UseMultiprocessorHalForUniprocessorPC();
			EnableMultiprocessorHal();
		}

		[Obsolete]
		private void UseMultiprocessorHalForUniprocessorPC()
		{
			var lineIndex = GetLineIndexByKey("Hal.Load", "acpiapic_up");
			const string updatedLine = "acpiapic_up = halmacpi.dll";
			UpdateLine(lineIndex, updatedLine);
		}

		[Obsolete]
		private void EnableMultiprocessorHal()
		{
			var lineIndex = GetLineIndexByKey("Hal.Load", "acpiapic_mp");
			const string updatedLine = "acpiapic_mp = halmacpi.dll";
			UpdateLine(lineIndex, updatedLine);
		}

		/// <summary>
		/// This method will remove the /noguiboot switch and will enable the Windows logo to be displayed during text-mode setup,
		/// This is useful because some programs (namely sanbootconf) will print valuable debug information on top of the Windows logo.
		/// Historical note: /noguiboot is required when using monitors that do not support VGA resolution. (text-mode setup uses EGA resolution)
		/// </summary>
		public void EnableGUIBoot()
		{
			string line;
			var lineIndex = GetLineIndexByKey("SetupData", "OsLoadOptions", out line);
			var keyAndValues = GetKeyAndValues(line);
			var options = keyAndValues.Value[0];
			options = Unquote(options);
			var optionList = StringUtils.Split(options, ' ');
			optionList.Remove("/noguiboot");
			options = StringUtils.Join(optionList, " ");
			options = Quote(options);
			var updatedLine = "OsLoadOptions = " + options;
			UpdateLine(lineIndex, updatedLine);
		}

		private int MinorOSVersion
		{
			get
			{
				var values = GetValuesOfKeyInSection("SetupData", "MinorVersion");
				var value = TryGetValue(values, 0);
				var minorOSVersion = Conversion.ToInt32(value, -1);
				if (minorOSVersion == -1)
				{
					Console.WriteLine("Error: '{0}' is corrupted.", FileName);
					Program.Exit();
				}
				return minorOSVersion;
			}
		}

		private static string GetSourceDisksFileDllEntry(string fileName, int minorOSVersion)
		{
			return GetSourceDisksFileEntry(1, (int)WinntDirectoryName.System32, fileName, false, FileCopyDisposition.AlwaysCopy, FileCopyDisposition.AlwaysCopy, string.Empty, minorOSVersion);
		}

		private static string GetSourceDisksFileDriverEntry(int sourceDiskID, string fileName, FileCopyDisposition fileCopyDisposition, string destinationFileName, int minorOSVersion)
		{
			return GetSourceDisksFileEntry(sourceDiskID, (int)WinntDirectoryName.System32_Drivers, fileName, true, fileCopyDisposition, fileCopyDisposition, destinationFileName, minorOSVersion);
		}

		// not sure about most of the values here
		// http://www.msfn.org/board/topic/26742-nlite-not-processing-layoutinf/page__st__13
		// http://www.msfn.org/board/topic/125480-txtsetupsif-syntax/
		/// <param name="textModeDisposition"></param>
		/// <param name="destinationFileName">leave Empty to keep the original name</param>
		/// <param name="sourceDiskID"></param>
		/// <param name="destinationWinntDirectoryID"></param>
		/// <param name="fileName"></param>
		/// <param name="isDriver"></param>
		/// <param name="upgradeDisposition"></param>
		/// <param name="minorOSVersion"></param>
		private static string GetSourceDisksFileEntry(int sourceDiskID, int destinationWinntDirectoryID, string fileName, bool isDriver, FileCopyDisposition upgradeDisposition, FileCopyDisposition textModeDisposition, string destinationFileName, int minorOSVersion)
		{
			// here is sourceDiskID - 1st value
			var subdir = string.Empty; // values encountered: string.Empty
			
			var size = string.Empty; // values encountered: string.Empty
			var checksum = string.Empty; // values encountered: string.Empty, v
			var unused1 = string.Empty; // values encountered: string.Empty
			var unused2 = string.Empty; // values encountered: string.Empty

			// I believe trailing underscore means compressed (because compressed files have trailing underscore in their extension)
			// leading underscore apparently means the file is subject to a file-size check when copied - http://www.msfn.org/board/topic/127677-txtsetupsif-layoutinf-reference/
			// values encountered: string.Empty, _1, _3, _5, _6, _7, _x, 2_, 3_, 4_, 5_, 6_
			// after looking at [SourceDisksNames] in txtsetup.sif it seems that bootMediaOrder is referring to the floppy disk number from which to copy the file
			var bootMediaOrder = isDriver
				? "4_"
				: string.Empty;

			// here is winntDirectoryID - 8th value

			// here is upgradeDisposition - 9th value, values encountered: 0,1,2,3 
			
			// here is textModeDisposition - 10th value, values encountered: string.Empty,0,1,2,3

			var line = string.Format("{0} = {1},{2},{3},{4},{5},{6},{7},{8},{9},{10}", fileName, sourceDiskID, subdir, size, checksum,
										unused1, unused2, bootMediaOrder, destinationWinntDirectoryID, (int)upgradeDisposition, (int)textModeDisposition);
			
			// here is destinationFileName - 11th value, actual filenames appear here (including long filenames)

			var appendXP2003DriverEntries = isDriver && (minorOSVersion != 0);

			if (destinationFileName != string.Empty || appendXP2003DriverEntries)
				line += "," + destinationFileName;

			// the next 2 entries are only present in Windows XP / 2003, and usually used with .sys / .dll
			// I could not figure out what they do, the presence / omittance of these entries do not seem to have any effect during text-mode phase
			// note that GUI mode may use txtsetup.sif as well (copied to %windir%\$winnt$.sif if installing from /makelocalsource)
			if (appendXP2003DriverEntries) 
			{
				const int unknownFlag = 1; // 12th value, values encountered: string.Empty,0,1
				var destinationDirectoryID = destinationWinntDirectoryID;
				line += string.Format(",{0},{1}", unknownFlag, destinationDirectoryID);
			}
			return line;
		}


		// Text-mode setup will always initialize services using the values stored under Services\serviceName
		// where serviceName is the service file name without the .sys extension
		public static string GetServiceName(string fileName)
		{
			var serviceName = fileName.Substring(0, fileName.Length - 4);
			return serviceName;
		}
	}

	// Windows 2000 upgradecode && newinstallcode:
	// 0 - Always copies the file
	// 1 - Copies the file only if it exists in the installation directory
	// 2 - Does not copy the file if it exists in the installation directory
	// 3 - Does not copy the file (DEFAULT)

	/// <summary>
	/// Note that TXTSETUP.INF has its own copy directives in the [SCSI.Load], [BootBusExtenders], [BusExtenders], [InputDevicesSupport] and [Keyboard] sections,
	/// which are activated only if the hardware is present.
	/// This means that a file may be copied even if its FileCopyDisposition is set to DoNotCopy.
	/// </summary>
	public enum FileCopyDisposition
	{
		AlwaysCopy,
		CopyOnlyIfAlreadyExists,
		DoNotCopyIfAlreadyExists, // [SourceDisksFiles] entry will be used by [SCSI.Load] to copy the file if the hardware is present
		DoNotCopy,				// [SourceDisksFiles] entry will be used by [SCSI.Load] to copy the file if the hardware is present
	}

	// ReSharper disable once InconsistentNaming
	public enum WinntDirectoryName
	{
		//Root = 1, // %windir%
		System32 = 2,
		System32_Drivers = 4,
		Temp = 45,
	}
}
