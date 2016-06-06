using System;
using System.Collections.Generic;
using System.IO;
using IntegrateDrv.Utilities.FileSystem;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public class PortableExecutableInfo
	{
		private byte[] _dosStubBytes; // DOS program stub is here ("This program cannot be run in DOS mode.")
		private byte[] _filler;
		private byte[] _remainingBytes; // Digital signature is here

		public PortableExecutableInfo(string path)
		{
			Sections = new List<byte[]>();
			SectionHeaders = new List<PESectionHeader>();
			var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
			var reader = new BinaryReader(stream);
			Parse(reader);
			reader.Close(); // closes the underlying stream as well
		}

		public PortableExecutableInfo(byte[] fileBytes)
		{
			Sections = new List<byte[]>();
			SectionHeaders = new List<PESectionHeader>();
			var stream = new MemoryStream(fileBytes);
			var reader = new BinaryReader(stream);
			Parse(reader);
			reader.Close(); // closes the underlying stream as well
		}

		public void Parse(BinaryReader reader)
		{
			DOSHeader = DOSHeader.Parse(reader);
			var dosStubSize = (int) (DOSHeader.CoffHeaderOffset - reader.BaseStream.Position);
			_dosStubBytes = reader.ReadBytes(dosStubSize);
			COFFHeader = COFFHeader.Parse(reader);
			PEHeaderOffset = (uint) reader.BaseStream.Position;
			PEHeader = PEHeader.Parse(reader);

			for (var i = 0; i < COFFHeader.NumberOfSections; i++)
				SectionHeaders.Add(PESectionHeader.Parse(reader));

			var fillerSize = (int) (SectionHeaders[0].PointerToRawData - reader.BaseStream.Position);
			_filler = reader.ReadBytes(fillerSize);

			for (var i = 0; i < COFFHeader.NumberOfSections; i++)
			{
				var sectionBytes = reader.ReadBytes((int) SectionHeaders[i].SizeOfRawData);
				Sections.Add(sectionBytes);
			}

			var remainingByteCount = (int) (reader.BaseStream.Length - reader.BaseStream.Position);
			_remainingBytes = reader.ReadBytes(remainingByteCount);
			// file ends here

			// Parse Import Directory:
			var importDirectoryEntry = PEHeader.DataDirectories[(int) DataDirectoryName.Import];
			if (importDirectoryEntry.VirtualAddress > 0)
			{
				var importDirectoryFileOffset = GetOffsetFromRVA(importDirectoryEntry.VirtualAddress);
				reader.BaseStream.Seek(importDirectoryFileOffset, SeekOrigin.Begin);
				ImportDirectory = ImportDirectory.Parse(reader);
			}
		}

		public void WritePortableExecutable(BinaryWriter writer)
		{
			writer.BaseStream.Seek(0, SeekOrigin.Begin);
			DOSHeader.Write(writer);
			writer.Write(_dosStubBytes);
			COFFHeader.Write(writer);
			PEHeaderOffset = (uint) writer.BaseStream.Position;
			PEHeader.Write(writer);
			for (var i = 0; i < COFFHeader.NumberOfSections; i++)
				SectionHeaders[i].Write(writer);

			writer.Write(_filler);
			for (var i = 0; i < COFFHeader.NumberOfSections; i++)
				writer.Write(Sections[i]);

			writer.Write(_remainingBytes);

			// Write Import Directory:
			var importDirectoryEntry = PEHeader.DataDirectories[(int) DataDirectoryName.Import];
			if (importDirectoryEntry.VirtualAddress > 0)
			{
				var importDirectoryFileOffset = GetOffsetFromRVA(importDirectoryEntry.VirtualAddress);
				writer.Seek((int) importDirectoryFileOffset, SeekOrigin.Begin);
				ImportDirectory.Write(writer);
			}

			// Update PE checksum:
			writer.Seek(0, SeekOrigin.Begin);
			var fileBytes = new byte[writer.BaseStream.Length];
			writer.BaseStream.Read(fileBytes, 0, (int) writer.BaseStream.Length);
			var checksumOffset = PEHeaderOffset + PEHeader.ChecksumRelativeAddress;
			var checksum = PortableExecutableUtils.CalculcateChecksum(fileBytes, checksumOffset);
			writer.Seek((int) checksumOffset, SeekOrigin.Begin);
			writer.Write(checksum);
			writer.Flush();
		}

		public PESectionHeader FindSectionByRVA(uint rva)
		{
			for (var i = 0; i < SectionHeaders.Count; i++)
			{
				var sectionStart = SectionHeaders[i].VirtualAdress;
				var sectionEnd = sectionStart + SectionHeaders[i].VirtualSize;

				if (rva >= sectionStart && rva < sectionEnd)
					return SectionHeaders[i];
			}

			return null;
		}

		public uint GetOffsetFromRVA(uint rva)
		{
			var sectionHeader = FindSectionByRVA(rva);
			if (sectionHeader == null)
				throw new Exception("Invalid PE file");

			var index = (sectionHeader.PointerToRawData + (rva - sectionHeader.VirtualAdress));
			return index;
		}

		public uint GetRVAFromAddressInSection(PESectionHeader sectionHeader, uint addressInSection)
		{
			var rva = addressInSection + sectionHeader.VirtualAdress;
			return rva;
		}

		public uint GetRVAFromOffset(PESectionHeader sectionHeader, uint offset)
		{
			var rva = offset + sectionHeader.VirtualAdress - sectionHeader.PointerToRawData;
			return rva;
		}

		public static uint GetAddressInSectionFromRVA(PESectionHeader sectionHeader, uint rva)
		{
			var addressInSection = rva - sectionHeader.VirtualAdress;
			return addressInSection;
		}

		public DOSHeader DOSHeader { get; private set; }

		public COFFHeader COFFHeader { get; private set; }

		public PEHeader PEHeader { get; private set; }

		public List<byte[]> Sections { get; private set; }

		public List<PESectionHeader> SectionHeaders { get; private set; }

		public uint PEHeaderOffset { get; private set; }

		public ImportDirectory ImportDirectory { get; private set; }

		public static void WritePortableExecutable(PortableExecutableInfo peInfo, string path)
		{
			FileSystemUtils.ClearReadOnlyAttribute(path);
			var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
			WritePortableExecutable(peInfo, stream);
			stream.Close();
		}

		public static void WritePortableExecutable(PortableExecutableInfo peInfo, Stream stream)
		{
			var writer = new BinaryWriter(stream);
			peInfo.WritePortableExecutable(writer);
			writer.Close();
		}
	}
}