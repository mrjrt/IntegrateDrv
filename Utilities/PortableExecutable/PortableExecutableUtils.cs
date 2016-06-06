using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public static class PortableExecutableUtils
	{
		private static BinaryReader GetBinaryReader(string path)
		{
			var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
			var reader = new BinaryReader(stream);
			return reader;
		}

		public static List<string> GetDependencies(string path)
		{
			var result = new List<string>();
			var peInfo = new PortableExecutableInfo(path);

			var dir = peInfo.ImportDirectory;
			if (dir != null)
			{
				var reader = GetBinaryReader(path);
				foreach (var desc in dir.Descriptors)
				{
					var fileNameOffset = peInfo.GetOffsetFromRVA(desc.NameRVA);
					reader.BaseStream.Seek(fileNameOffset, SeekOrigin.Begin);
					var fileName = BinaryReaderUtils.ReadNullTerminatedAsciiString(reader);
					result.Add(fileName);
				}
				reader.Close();
			}
			return result;
		}

		public static void RenameDependencyFileName(string filePath, string oldFileName, string newFileName)
		{
			if (oldFileName.Length != newFileName.Length)
				throw new NotImplementedException(
					"when renaming dependencies, old file name must have the same size as new file name");

			var peInfo = new PortableExecutableInfo(filePath);
			uint oldNameRVA = 0;
			PESectionHeader header = null;
			var sectionIndex = -1;

			foreach (var descriptor in peInfo.ImportDirectory.Descriptors)
			{
				var nameRVA = descriptor.NameRVA;
				header = peInfo.FindSectionByRVA(nameRVA);
				if (header != null)
				{
					sectionIndex = peInfo.SectionHeaders.IndexOf(header);

					var fileNameAddressInSection = PortableExecutableInfo.GetAddressInSectionFromRVA(header, nameRVA);

					var fileName = ReadNullTerminatedAsciiString(peInfo.Sections[sectionIndex], fileNameAddressInSection);
					if (fileName.Equals(oldFileName, StringComparison.InvariantCultureIgnoreCase))
						oldNameRVA = nameRVA;
				}
			}

			if (oldNameRVA > 0)
			{
				var newFileNameAsciiBytes = Encoding.ASCII.GetBytes(newFileName);
				var addressInSection = PortableExecutableInfo.GetAddressInSectionFromRVA(header, oldNameRVA);
				var section = peInfo.Sections[sectionIndex];
				Buffer.BlockCopy(newFileNameAsciiBytes, 0, section, (int)addressInSection, newFileNameAsciiBytes.Length);
			}
			PortableExecutableInfo.WritePortableExecutable(peInfo, filePath);
		}

		private static string ReadNullTerminatedAsciiString(byte[] bytes, uint startIndex)
		{
			var index = (int)startIndex;
			using (var ms = new MemoryStream())
			{
				Byte lastByte;

				do
				{
					lastByte = bytes[index];
					ms.WriteByte(lastByte);
					index++;
				}
				while ((lastByte > 0) && (index < bytes.Length));

				return Encoding.ASCII.GetString(ms.GetBuffer(), 0, (Int32)(ms.Length - 1));
			}
		}

		// Adapted from:
		// http://stackoverflow.com/questions/6429779/can-anyone-define-the-windows-pe-checksum-algorithm
		/// <param name="checksumOffset">offset of the checksum withing the file</param>
		public static uint CalculcateChecksum(byte[] fileBytes, uint checksumOffset)
		{
			long checksum = 0;
			var top = Math.Pow(2, 32);

			var remainder = fileBytes.Length % 4;
			byte[] paddedFileBytes;
			if (remainder > 0)
			{
				paddedFileBytes = new byte[fileBytes.Length + 4 - remainder];
				Buffer.BlockCopy(fileBytes, 0, paddedFileBytes, 0, fileBytes.Length);
			}
			else
				// no need to pad
				paddedFileBytes = fileBytes;

			for (var i = 0; i < paddedFileBytes.Length / 4; i++)
			{
				if (i == checksumOffset/4)
					continue;
				var dword = BitConverter.ToUInt32(paddedFileBytes, i * 4);
				checksum = (checksum & 0xffffffff) + dword + (checksum >> 32);
				if (checksum > top)
					checksum = (checksum & 0xffffffff) + (checksum >> 32);
			}

			checksum = (checksum & 0xffff) + (checksum >> 16);
			checksum = (checksum) + (checksum >> 16);
			checksum = checksum & 0xffff;

			// The length is the one from the original fileBytes, not the padded one
			checksum += (uint)fileBytes.Length;
			return (uint)checksum;
		}
	}
}
