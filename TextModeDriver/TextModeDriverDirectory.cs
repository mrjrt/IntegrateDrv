using IntegrateDrv.Utilities.FileSystem;

namespace IntegrateDrv.TextModeDriver
{
	public class TextModeDriverDirectory
	{
		public TextModeDriverDirectory(string path)
		{
			Path = path;
			if (ContainsOEMSetupFile)
			{
				TextModeDriverSetupINI = new TextModeDriverSetupINIFile();
				TextModeDriverSetupINI.ReadFromDirectory(path);
			}
		}

		public bool ContainsOEMSetupFile
		{
			get { return FileSystemUtils.IsFileExist(Path + "txtsetup.oem"); }
		}

		public TextModeDriverSetupINIFile TextModeDriverSetupINI { get; private set; }

		public string Path { get; private set; }
	}
}