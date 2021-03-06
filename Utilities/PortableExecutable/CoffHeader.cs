//---------------------------------------------------------------------
// Authors: jachymko
//
// Description: Class which describes a COFF header.
//
// Creation Date: Dec 24, 2006
//---------------------------------------------------------------------
// Adapted by Tal Aloni, 2011.09.09

using System;
using System.IO;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	//using Runtime.Serialization.Binary;

	// Coff - Common Object File Format
	public sealed class COFFHeader
	{
		const uint NTSignature = 0x4550; // PE00

		// ReSharper disable MemberCanBePrivate.Global
		public COFFMachine Machine;
		public ushort NumberOfSections;
		//public DateTime TimeDateStamp;
		public uint TimeDateStamp;
		public uint PointerToSymbolTable;
		public uint NumberOfSymbols;
		public ushort SizeOfOptionalHeader;
		public CoffFlags Characteristics;
		// ReSharper restore MemberCanBePrivate.Global

		public static COFFHeader Parse(BinaryReader reader)
		{
			var signature = reader.ReadUInt32();
			if (NTSignature != signature)
				throw new Exception("Invalid COFF header signature");
			var header = new COFFHeader
			{
				Machine = (COFFMachine) reader.ReadUInt16(),
				NumberOfSections = reader.ReadUInt16(),
				TimeDateStamp = reader.ReadUInt32(),
				PointerToSymbolTable = reader.ReadUInt32(),
				NumberOfSymbols = reader.ReadUInt32(),
				SizeOfOptionalHeader = reader.ReadUInt16(),
				Characteristics = (CoffFlags) reader.ReadUInt16()
			};

			return header;
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write(NTSignature);
			writer.Write((ushort)Machine);
			writer.Write(NumberOfSections);
			writer.Write(TimeDateStamp);
			writer.Write(PointerToSymbolTable);
			writer.Write(NumberOfSymbols);
			writer.Write(SizeOfOptionalHeader);
			writer.Write((ushort)Characteristics);
		}
	}

	public enum COFFMachine : ushort
	{
		// ReSharper disable UnusedMember.Global
		Unknown = 0x0000,
		I386 = 0x014c,
		IA64 = 0x0200,
		Amd64 = 0x8664,
		// ReSharper restore UnusedMember.Global
	}

	[Flags]
	public enum CoffFlags : ushort
	{
		// ReSharper disable UnusedMember.Global
		RelocsStripped = 0x0001,
		ExecutableImage = 0x0002,
		LineNumsStripped = 0x0004,
		LocalSymsStripped = 0x0008,
		AggresiveWsTrim = 0x0010,
		LargeAddressAware = 0x0020,
		BytesReversedLow = 0x0080,
		Machine32Bit = 0x0100,
		DebugStripped = 0x0200,
		RemovableRunFromSwap = 0x0400,
		NetworkRunFromSwap = 0x0800,
		System = 0x1000,
		Dll = 0x2000,
		UniProcOnly = 0x4000,
		BytesReversedHi = 0x8000,
		// ReSharper restore UnusedMember.Global
	}
}
