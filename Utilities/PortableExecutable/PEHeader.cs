//---------------------------------------------------------------------
// Authors: jachymko
//
// Description: Class which describes a Portable Executable header.
//
// Creation Date: Dec 24, 2006
//---------------------------------------------------------------------
// Adapted by Tal Aloni, 2011.09.09

using System;
using System.IO;

// ReSharper disable once UnusedMember.Global

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public sealed class PEHeader
	{
		// ReSharper disable MemberCanBePrivate.Global
		public const int ChecksumRelativeAddress = 64;
		public PEHeaderType Type;

		public Version LinkerVersion;
		public Version OperatingSystemVersion;
		public Version ImageVersion;
		public Version SubsystemVersion;
		public uint SizeOfCode;
		public uint SizeOfInitializedData;
		public uint SizeOfUninitializedData;
		public uint AddressOfEntryPoint;
		public uint BaseOfCode;
		public uint BaseOfData;

		public ulong ImageBase;
		public uint SectionAlignment;
		public uint FileAlignment;
		public uint Win32VersionValue;
		public uint SizeOfImage;
		public uint SizeOfHeaders;
		public uint Checksum;
		public PESubsystem Subsystem;
		public PEDllCharacteristics DllCharacteristics;
		public ulong SizeOfStackReserve;
		public ulong SizeOfStackCommit;
		public ulong SizeOfHeapReserve;
		public ulong SizeOfHeapCommit;
		public uint LoaderFlags;
		// ReSharper restore MemberCanBePrivate.Global

		public PEDataDirectory[] DataDirectories;

		public PEDataDirectory GetDataDirectory(DataDirectoryName index)
		{
			return DataDirectories[(int)(index)];
		}

		public static PEHeader Parse(BinaryReader reader)
		{
			var signature = (PEHeaderType)reader.ReadUInt16();
			if (!Enum.IsDefined(typeof (PEHeaderType), signature))
				throw new Exception("Invalid PE header signature");

			var header = new PEHeader
			{
				Type = signature,
				LinkerVersion = new Version(reader.ReadByte(), reader.ReadByte()),
				SizeOfCode = reader.ReadUInt32(),
				SizeOfInitializedData = reader.ReadUInt32(),
				SizeOfUninitializedData = reader.ReadUInt32(),
				AddressOfEntryPoint = reader.ReadUInt32(),
				BaseOfCode = reader.ReadUInt32()
			};

			if (signature == PEHeaderType.PE64)
				header.ImageBase = reader.ReadUInt64();
			else
			{
				header.BaseOfData = reader.ReadUInt32();
				header.ImageBase = reader.ReadUInt32();
			}

			header.SectionAlignment = reader.ReadUInt32();
			header.FileAlignment = reader.ReadUInt32();
			header.OperatingSystemVersion = new Version(reader.ReadUInt16(), reader.ReadUInt16());
			header.ImageVersion = new Version(reader.ReadUInt16(), reader.ReadUInt16());
			header.SubsystemVersion = new Version(reader.ReadUInt16(), reader.ReadUInt16());
			header.Win32VersionValue = reader.ReadUInt32();
			header.SizeOfImage = reader.ReadUInt32();
			header.SizeOfHeaders = reader.ReadUInt32();
			header.Checksum = reader.ReadUInt32();
			header.Subsystem = (PESubsystem)reader.ReadUInt16();
			header.DllCharacteristics = (PEDllCharacteristics)reader.ReadUInt16();

			if (signature == PEHeaderType.PE64)
			{
				header.SizeOfStackReserve = reader.ReadUInt64();
				header.SizeOfStackCommit = reader.ReadUInt64();
				header.SizeOfHeapReserve = reader.ReadUInt64();
				header.SizeOfHeapCommit = reader.ReadUInt64();
			}
			else
			{
				header.SizeOfStackReserve = reader.ReadUInt32();
				header.SizeOfStackCommit = reader.ReadUInt32();
				header.SizeOfHeapReserve = reader.ReadUInt32();
				header.SizeOfHeapCommit = reader.ReadUInt32();
			}

			header.LoaderFlags = reader.ReadUInt32();

			header.DataDirectories = new PEDataDirectory[reader.ReadUInt32()];
			for (var i = 0; i < header.DataDirectories.Length; i++)
				header.DataDirectories[i] = PEDataDirectory.Parse(reader);

			return header;
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write((ushort)Type);
			writer.Write((byte)LinkerVersion.Major);
			writer.Write((byte)LinkerVersion.Minor);
			writer.Write(SizeOfCode);
			writer.Write(SizeOfInitializedData);
			writer.Write(SizeOfUninitializedData);
			writer.Write(AddressOfEntryPoint);
			writer.Write(BaseOfCode);

			if (Type == PEHeaderType.PE64)
				writer.Write(ImageBase);
			else
			{
				writer.Write(BaseOfData);
				writer.Write((uint) ImageBase);
			}

			writer.Write(SectionAlignment);
			writer.Write(FileAlignment);
			writer.Write((ushort)OperatingSystemVersion.Major);
			writer.Write((ushort)OperatingSystemVersion.Minor);
			writer.Write((ushort)ImageVersion.Major);
			writer.Write((ushort)ImageVersion.Minor);
			writer.Write((ushort)SubsystemVersion.Major);
			writer.Write((ushort)SubsystemVersion.Minor);
			writer.Write(Win32VersionValue);
			writer.Write(SizeOfImage);
			writer.Write(SizeOfHeaders);
			writer.Write(Checksum);
			writer.Write((ushort)Subsystem);
			writer.Write((ushort)DllCharacteristics);

			if (Type == PEHeaderType.PE64)
			{
				writer.Write(SizeOfStackReserve);
				writer.Write(SizeOfStackCommit);
				writer.Write(SizeOfHeapReserve);
				writer.Write(SizeOfHeapCommit);
			}
			else
			{
				writer.Write((uint)SizeOfStackReserve);
				writer.Write((uint)SizeOfStackCommit);
				writer.Write((uint)SizeOfHeapReserve);
				writer.Write((uint)SizeOfHeapCommit);
			}
			writer.Write(LoaderFlags);

			writer.Write(DataDirectories.Length);
			for (var i = 0; i < DataDirectories.Length; i++)
				DataDirectories[i].Write(writer);
		}
	}

	public enum DataDirectoryName
	{
		// ReSharper disable UnusedMember.Global
		Export,
		Import,
		Resource,
		Exception,
		Security,
		BaseRelocationTable,
		Debug,
		ArchitectureData,
		GlobalPointer,
		ThreadLocalStorage,
		LoadConfiguration,
		BoundImport,
		ImportAddressTable,
		DelayLoadImport,
		CorHeader,
		// ReSharper restore UnusedMember.Global
	}

	public sealed class PEDataDirectory
	{
		// ReSharper disable MemberCanBePrivate.Global
		public uint VirtualAddress;
		public uint Size;
		// ReSharper restore MemberCanBePrivate.Global

		public static PEDataDirectory Parse(BinaryReader reader)
		{
			var dir = new PEDataDirectory
			{
				VirtualAddress = reader.ReadUInt32(),
				Size = reader.ReadUInt32(),
			};
			return dir;
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write(VirtualAddress);
			writer.Write(Size);
		}

		public override string ToString()
		{
			return string.Format("PEDataDirectory, RVA=0x{0:x}, Size=0x{1:x}",
				VirtualAddress, Size);
		}
	}

	public enum PEHeaderType : ushort
	{
		// ReSharper disable UnusedMember.Global
		PE32 = 0x10b,
		PE64 = 0x20b,
		RomImage = 0x107,
		// ReSharper restore UnusedMember.Global
	}

	public enum PESubsystem : ushort
	{
		// ReSharper disable UnusedMember.Global
		Unknown = 0,
		Native = 1,
		Windows = 2,
		WindowsConsole = 3,
		OS2 = 5,
		Posix = 7,
		WindowsCE = 9,
		EfiApplication = 10,
		EfiBootServiceDriver = 11,
		EfiRuntimeDriver = 12,
		EfiRomImage = 13,
		Xbox = 14,
		WindowsBootApplication = 16,
		// ReSharper restore UnusedMember.Global
	}

	public enum PEDllCharacteristics : ushort
	{
		// ReSharper disable UnusedMember.Global
		DynamicBase = 0x0040,
		ForceIntegrity = 0x0080,
		NXCompatible = 0x0100,
		NoIsolation = 0x0200,
		NoSeh = 0x0400,
		NoBind = 0x0800,
		WdmDriver = 0x2000,
		TerminalServicesAware = 0x8000,
		// ReSharper restore UnusedMember.Global
	}
}
