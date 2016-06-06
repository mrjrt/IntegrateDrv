using IntegrateDrv.BaseClasses;

namespace IntegrateDrv.WindowsDirectory
{
	public class DOSNetINFFile : INIFile
	{
		private const string SetupDirectoryID = "d1";

		public DOSNetINFFile() : base("dosnet.inf")
		{
		}

		/// <summary>
		/// Necessary for files to be copied
		/// </summary>
		/// <param name="sourceDirectoryPath">
		/// In the form: 'SCSIDRV' or 'SCSIDRV\AMDAHCI'
		/// </param>
		private void AddOptionalSourceDirectory(string sourceDirectoryPath)
		{
			if (!SectionNames.Contains("OptionalSrcDirs"))
				AddSection("OptionalSrcDirs");
			if (GetLineIndex("OptionalSrcDirs", sourceDirectoryPath) == -1)
				AppendLineToSection("OptionalSrcDirs", sourceDirectoryPath);
		}

		private void AddSourceDirectory(string directoryID, string sourceDirectoryPathInMediaRootForm)
		{
			var line = string.Format("{0} = {1}", directoryID, sourceDirectoryPathInMediaRootForm);
			AppendLineToSection("Directories", line);

			if (sourceDirectoryPathInMediaRootForm.ToLower().StartsWith("\\i386\\"))
				AddOptionalSourceDirectory(sourceDirectoryPathInMediaRootForm.Substring(6));
			else if (sourceDirectoryPathInMediaRootForm.ToLower().StartsWith("\\amd64\\"))
				AddOptionalSourceDirectory(sourceDirectoryPathInMediaRootForm.Substring(7));
			else if (sourceDirectoryPathInMediaRootForm.ToLower().StartsWith("\\ia64\\"))
				AddOptionalSourceDirectory(sourceDirectoryPathInMediaRootForm.Substring(6));
		}

		private string GetSourceDirectoryID(string sourceDirectoryPathInMediaRootForm)
		{
			var section = GetSection("Directories");
			foreach (var line in section)
			{
				var keyAndValues = GetKeyAndValues(line);
				var path = keyAndValues.Value[0];
				if (path == sourceDirectoryPathInMediaRootForm)
					return keyAndValues.Key;
			}
			return string.Empty;
		}

		private string AllocateSourceDirectoryID(string sourceDirectoryPathInMediaRootForm)
		{
			var sourceDirectoryID = GetSourceDirectoryID(sourceDirectoryPathInMediaRootForm);
			if (sourceDirectoryID == string.Empty)
			{
				var index = 11;
				while (IsDirectoryIDTaken("d" + index))
					index++;

				sourceDirectoryID = "d" + index;
				AddSourceDirectory(sourceDirectoryID, sourceDirectoryPathInMediaRootForm);
			}
			return sourceDirectoryID;
		}

		public void InstructSetupToCopyFileFromSetupDirectoryToLocalSourceDriverDirectory(string sourceDirectoryInMediaRootForm, string fileName)
		{
			var sourceDirectoryID = AllocateSourceDirectoryID(sourceDirectoryInMediaRootForm);

			var line = string.Format("{0},{1}", sourceDirectoryID, fileName);
			if (GetLineIndex("Files", line) == -1)
				AppendLineToSection("Files", line);
		}

		public void InstructSetupToCopyFileFromSetupDirectoryToLocalSourceRootDirectory(string fileName)
		{
			InstructSetupToCopyFileFromSetupDirectoryToLocalSourceRootDirectory(string.Empty, fileName);
		}

		public void InstructSetupToCopyFileFromSetupDirectoryToLocalSourceRootDirectory(string sourceFilePath, string fileName)
		{
			var line = string.Format("{0},{1}", SetupDirectoryID, fileName);
			if (sourceFilePath != string.Empty)
				line += "," + sourceFilePath;
			if (GetLineIndex("Files", line) == -1)
				AppendLineToSection("Files", line);
		}

		public void InstructSetupToCopyFileFromSetupDirectoryToBootDirectory(string fileName)
		{
			InstructSetupToCopyFileFromSetupDirectoryToBootDirectory(string.Empty, fileName);
		}

		public void InstructSetupToCopyFileFromSetupDirectoryToBootDirectory(string sourceFilePath, string fileName)
		{
			var line = string.Format("{0},{1}", SetupDirectoryID, fileName);
			if (sourceFilePath != string.Empty)
				line += "," + sourceFilePath;
			if (GetLineIndex("FloppyFiles.1", line) == -1)
				AppendLineToSection("FloppyFiles.1", line);
		}

		private bool IsDirectoryIDTaken(string directoryID)
		{
			var section = GetSection("Directories");
			foreach (var line in section)
			{
				var keyAndValues = GetKeyAndValues(line);
				if (keyAndValues.Key.Equals(directoryID))
					return true;
			}
			return false;
		}
	}
}
