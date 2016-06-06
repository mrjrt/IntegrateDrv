using System;
using System.IO;

namespace IntegrateDrv.Utilities.FileSystem
{
	public static class FileSystemUtils
	{
		public static bool IsDirectoryExist(string path)
		{
			var dir = new DirectoryInfo(path);
			return dir.Exists;
		}

		public static bool IsFileExist(string path)
		{
			return File.Exists(path);
		}

		public static void CreateDirectory(string path)
		{
			Directory.CreateDirectory(path);
		}

		/// <summary>
		/// This Method does not support files with length over 4GB
		/// </summary>
		public static byte[] ReadFile(string path)
		{
			var fileStream = new FileStream(path,FileMode.Open, FileAccess.Read);
			var fileLength = Convert.ToInt32(fileStream.Length);
			var fileBytes = new byte[fileLength];

			fileStream.Read(fileBytes,0,fileLength);
			
			fileStream.Close();
			return fileBytes;
		}

		public static void ClearReadOnlyAttribute(string path)
		{
			var file = new FileInfo(path);
			if (file.Exists)
			{
				file.IsReadOnly = false;
			}
		}

		public static void WriteFile(string path, byte[] bytes)
		{
			WriteFile(path, bytes, FileMode.Create);
		}

		private static void WriteFile(string path, byte[] bytes, FileMode fileMode)
		{
			var file = new FileInfo(path);

			var stream = file.Open(fileMode, FileAccess.Write);
			stream.Write(bytes, 0, bytes.Length);
			stream.Close();
		}

		/// <summary>
		/// Extracts file / directory name from path
		/// </summary>
		public static string GetNameFromPath(string path)
		{
			var parts = path.Split('\\');
			if (parts.Length > 0)
			{
				return parts[parts.Length - 1] == string.Empty
					? parts[parts.Length - 2]
					: parts[parts.Length - 1];
			}

			return string.Empty;
		}
	}
}
