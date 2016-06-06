namespace IntegrateDrv.WindowsDirectory
{
	public partial class TextSetupINFFile
	{
		// if no group is specified for the driver in setupreg.hiv, then sxtsetup.sif will determine driver initialization order:
		// (setupreg.hiv has precedence, apparently everything not present in setupreg.hiv will be initialized by it's load order, which is determined by txtsetup.sif [xxxx.Load] sections)
		// this is txtsetup.sif initialization order:
		// (Note: when adding entries to other sections they do not appear to be loaded)
		//
		// [BootBusExtenders.Load]	  // sticky! (any entry added to here will create its own Services\serviceName key and values, which will run over hivesys.inf entries)
		// [BusExtenders.Load]		  // sticky!
		// [InputDevicesSupport.Load]   // partially sticky!, group name won't be created / ran-over, service "Start" entry will be set to 3!
		// [Keyboard.Load]
		// [SCSI.Load]				  // sticky! + has its own unavoidable file copy mechanism.
		// [DiskDrivers.Load]
		// [ScsiClass.Load]
		// [FileSystems.Load]
		// [CdRomDrivers.Load]


		// Note: [CdRomDrivers.Load] will load entries even if no cd-rom drive is present
		// Note: [FloppyDrivers.Load] will NOT load entries even if floppy is present

		public void InstructToLoadBootBusExtenderDriver(string filename, string deviceName)
		{
			InstructToLoadBootBusExtenderDriver(filename, deviceName, string.Empty);
		}

		public void InstructToLoadBootBusExtenderDriver(string filename, string deviceName, string filesSectionName)
		{
			InstructToLoadGroupDriver("BootBusExtenders", filename, deviceName, GroupDriverFormat.TitleAndFilesSectionNameAndServiceName, filesSectionName);
		}

		public void InstructToLoadBusExtenderDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("BusExtenders", filename, deviceName, GroupDriverFormat.TitleAndFilesSectionNameAndServiceName);
		}

		public void InstructToLoadInputDevicesSupportDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("InputDevicesSupport", filename, deviceName, GroupDriverFormat.TitleAndFilesSectionNameAndServiceName);
		}

		public void InstructToLoadKeyboardDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("Keyboard", filename, deviceName, GroupDriverFormat.TitleAndFilesSectionNameAndServiceName);
		}

		/// <summary>
		/// Each SCSI driver load entry is also a copy directive
		/// </summary>
		public void InstructToLoadSCSIDriver(string filename, string deviceName)
		{
			InstructToLoadSCSIDriver(filename, deviceName, string.Empty);
		}

		/// <summary>
		/// Each SCSI driver load entry is also a copy directive
		/// </summary>
		public void InstructToLoadSCSIDriver(string filename, string deviceName, string destinationFileName)
		{
			InstructToLoadSCSIDriver(filename, deviceName, (int)WinntDirectoryName.System32_Drivers, destinationFileName);
		}

		/// <summary>
		/// Each SCSI driver load entry is also a copy directive
		/// </summary>
		public void InstructToLoadSCSIDriver(string filename, string deviceName, int destinationWinntDirectoryID)
		{
			InstructToLoadSCSIDriver(filename, deviceName, destinationWinntDirectoryID, string.Empty);
		}
		/// <summary>
		/// Each SCSI driver load entry is also a copy directive
		/// </summary>
		public void InstructToLoadSCSIDriver(string filename, string deviceName, int destinationWinntDirectoryID, string destinationFileName)
		{
			InstructToLoadGroupDriver("SCSI", filename, deviceName, GroupDriverFormat.SCSI, string.Empty, destinationWinntDirectoryID, destinationFileName);
		}

		public void InstructToLoadDiskDriversDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("DiskDrivers", filename, deviceName, GroupDriverFormat.Title);
		}

		public void InstructToLoadScsiClassDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("ScsiClass", filename, deviceName, GroupDriverFormat.Title);
		}

		public void InstructToLoadFileSystemsDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("FileSystems", filename, deviceName, GroupDriverFormat.Title);
		}

		public void InstructToLoadCdRomDriversDriver(string filename, string deviceName)
		{
			InstructToLoadGroupDriver("CdRomDrivers", filename, deviceName, GroupDriverFormat.Title);
		}

		private void InstructToLoadGroupDriver(string groupName, string filename, string deviceName, GroupDriverFormat format)
		{
			InstructToLoadGroupDriver(groupName, filename, deviceName, format, string.Empty);
		}

		private void InstructToLoadGroupDriver(string groupName, string filename, string deviceName, GroupDriverFormat format, string filesSectionName)
		{
			InstructToLoadGroupDriver(groupName, filename, deviceName, format, filesSectionName, 0, string.Empty);
		}

		/// <param name="format"></param>
		/// <param name="filesSectionName">Note that files listed in this section will only be copied if the device is present</param>
		/// <param name="groupName"></param>
		/// <param name="filename"></param>
		/// <param name="deviceName"></param>
		/// <param name="scsiDriverDestinationWinntDirectoryID"></param>
		/// <param name="scsiDriverDestinationFileName"></param>
		private void InstructToLoadGroupDriver(string groupName, string filename, string deviceName, GroupDriverFormat format, string filesSectionName, int scsiDriverDestinationWinntDirectoryID, string scsiDriverDestinationFileName)
		{
			var serviceName = GetServiceName(filename);
			// the connection between what I call entryKey and serviceName is a little vague to me,
			// the HardwareIDDatabase entry specify the name of the service subkey under CurrentControlSet\Services (a.k.a. serviceName),
			// which *must* match the file name of the service without the .sys extension.
			// (text-mode setup will always initialize services using the values stored under Services\serviceName (where serviceName is the service file name without the .sys extension)
			// however, the entryKey which specify the initalization order of the service under [xxxx] and [xxxx.Load] and its display name
			// can apparently use whatever value it likes, as long as it match in both sections.

			// we just use the serviceName, this is how it's almost always done in txtsetup.sif (two exceptions are in the [Keyboard] section and the [Hal] sections)
			var entryKey = serviceName;
			var loadLineIndex = GetLineIndexByKey(groupName + ".Load", entryKey);
			var loadLine = string.Format("{0} = {1}", entryKey, filename);
			if (format == GroupDriverFormat.SCSI)
			{
				// [SCSI.Load] section:
				// Setup will not start copying files during the copy phase unless the second argument (WinntDirectoryID) is present and is a valid Winnt Directory ID.
				// note that setup will demand [SourceDisksFiles] entry to be present for the file, but this is a separate copy directive, and we have to make sure
				// that only one copy operation (followed by deletion of the source file) will be taking place
				loadLine += "," + scsiDriverDestinationWinntDirectoryID;
				if (scsiDriverDestinationFileName != string.Empty) // discovered this by trial and error
				{
					loadLine += "," + scsiDriverDestinationFileName;
				}
			}

			if (loadLineIndex == -1)
				AppendLineToSection(groupName + ".Load", loadLine);
			else
				UpdateLine(loadLineIndex, loadLine);

			// Add title
			var titleLineIndex = GetLineIndexByKey(groupName, entryKey);
			var titleLine = string.Format("{0} = {1}",entryKey, Quote(deviceName));
			if (format == GroupDriverFormat.TitleAndFilesSectionName || 
				format == GroupDriverFormat.TitleAndFilesSectionNameAndServiceName)
			{
				if (string.IsNullOrEmpty(filesSectionName))
					filesSectionName = "files.none";
				titleLine += "," + filesSectionName;
			}

			if (format == GroupDriverFormat.TitleAndFilesSectionNameAndServiceName)
				titleLine += "," + serviceName;

			if (titleLineIndex == -1)
				AppendLineToSection(groupName, titleLine);
			else
				UpdateLine(titleLineIndex, titleLine);
		}

		public void RemoveInputDevicesSupportDriverLoadInstruction(string serviceName)
		{
			RemoveGroupDriverLoadInstruction("InputDevicesSupport", serviceName);
		}

		private void RemoveGroupDriverLoadInstruction(string groupName, string serviceName)
		{
			var entryKey = serviceName;
			var loadLineIndex = GetLineIndexByKey(groupName + ".Load", entryKey);
			if (loadLineIndex >= 0)
				DeleteLine(loadLineIndex);

			// Remove title
			var titleLineIndex = GetLineIndexByKey(groupName, entryKey);
			if (titleLineIndex >= 0)
				DeleteLine(titleLineIndex);
		}
	}

	public enum GroupDriverFormat
	{
		Title,
		TitleAndFilesSectionName,
		TitleAndFilesSectionNameAndServiceName,
		SCSI, // [SCSI] contains title, and [SCSI.Load] also contains destinationWinntDirectoryID and destinationFileName
	}
}
