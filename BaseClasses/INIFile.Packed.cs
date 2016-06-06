using System;
using System.Collections.Generic;
using System.IO;
using IntegrateDrv.Utilities.FileSystem;
using Microsoft.Deployment.Compression;
using Microsoft.Deployment.Compression.Cab;

namespace IntegrateDrv.BaseClasses
{
	public partial class INIFile
	{
		private void ReadPacked(string filePath)
		{
			var bytes = FileSystemUtils.ReadFile(filePath);
			var unpackedBytes = Unpack(bytes, FileName);
			_encoding = GetEncoding(ref unpackedBytes);
			Text = _encoding.GetString(unpackedBytes);
		}

		public void ReadPackedFromDirectory(string directoryPath)
		{
			if (FileName == string.Empty)
				throw new Exception("ReadFileFromDirectory - class has not been initizalized with a file name");
			ReadPacked(directoryPath + PackedFileName);
		}

		public void ReadPackedCriticalFileFromDirectory(string directoryPath)
		{
			try
			{
				ReadPackedFromDirectory(directoryPath);
			}
			catch (CabException)
			{
				Console.WriteLine("Error: Cannot unpack '{0}', Cab file is corrupted.", PackedFileName);
				Program.Exit();
			}
		}

		private void SavePacked(string path)
		{
			// if an existing file was read, this.Text will contain the BOM character, otherwise we write ASCII and there is no need for BOM.
			var unpackedBytes = _encoding.GetBytes(Text);
			var bytes = Pack(unpackedBytes, FileName);
			FileSystemUtils.ClearReadOnlyAttribute(path);
			FileSystemUtils.WriteFile(path, bytes);
			IsModified = false;
		}

		public void SavePackedToDirectory(string directory)
		{
			SavePacked(directory + PackedFileName);
		}

		public static byte[] Pack(byte[] unpackedBytes, string unpackedFileName)
		{
			var unpackedStream = new MemoryStream(unpackedBytes);
			var streamContext = new BasicPackStreamContext(unpackedStream);

			var fileNames = new List<string> {unpackedFileName};
			using (var engine = new CabEngine())
			{
				engine.Pack(streamContext, fileNames);
			}
			var packedStream = streamContext.ArchiveStream;
			if (packedStream != null)
			{
				packedStream.Position = 0;

				var packedBytes = new byte[packedStream.Length];
				packedStream.Read(packedBytes, 0, packedBytes.Length);
				return packedBytes;
			}
			var message = string.Format("Error: File '{0}' failed to be repacked");
			Console.WriteLine(message);
			Program.Exit();
			throw new Exception(message);
		}

		public static byte[] Unpack(byte[] fileBytes, string unpackedFileName)
		{
			var packedStream = new MemoryStream(fileBytes);
			var streamContext = new BasicUnpackStreamContext(packedStream);

			Predicate<string> isFileMatch =
				match =>
					String.Compare(match, unpackedFileName, StringComparison.OrdinalIgnoreCase) == 0;
			using (var engine = new CabEngine())
			{
				engine.Unpack(streamContext, isFileMatch);
			}
			var unpackedStream = streamContext.FileStream;
			if (unpackedStream != null)
			{
				unpackedStream.Position = 0;

				var unpackedBytes = new byte[unpackedStream.Length];
				unpackedStream.Read(unpackedBytes, 0, unpackedBytes.Length);

				return unpackedBytes;
			}
			var message = string.Format("Error: File does not contain the expected file ('{1}')", unpackedFileName);
			Console.WriteLine(message);
			Program.Exit();
			return new byte[0];
		}

		public string PackedFileName
		{
			get { return FileName.Substring(0, FileName.Length - 1) + "_"; }
		}
	}
}