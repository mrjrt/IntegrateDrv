//---------------------------------------------------------------------
// Authors: jachymko
//
// Description: Class which describes a DOS header.
//
// Creation Date: Dec 24, 2006
//---------------------------------------------------------------------
// Adapted by Tal Aloni, 2011.09.09

using System;
using System.IO;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public sealed class DOSHeader
	{
		public const ushort DOSSignature = 0x5a4d; // MZ

		public ushort BytesOnLastPage;
		public ushort PageCount;
		public ushort RelocationCount;
		public ushort HeaderSize;
		public ushort MinExtraParagraphs;
		public ushort MaxExtraParagraphs;
		public ushort InitialSS;
		public ushort InitialSP;
		public ushort Checksum;
		public ushort InitialIP;
		public ushort InitialCS;
		public ushort RelocationTableOffset;
		public ushort OverlayNumber;
		public ushort OemID;
		public ushort OemInfo;
		public uint CoffHeaderOffset;

		public static DOSHeader Parse(BinaryReader reader)
		{
			var signature = reader.ReadUInt16();
			if (DOSSignature != signature)
				throw new Exception("Invalid DOS header signature");

			var header = new DOSHeader
			{
				BytesOnLastPage = reader.ReadUInt16(),
				PageCount = reader.ReadUInt16(),
				RelocationCount = reader.ReadUInt16(),
				HeaderSize = reader.ReadUInt16(),
				MinExtraParagraphs = reader.ReadUInt16(),
				MaxExtraParagraphs = reader.ReadUInt16(),
				InitialSS = reader.ReadUInt16(),
				InitialSP = reader.ReadUInt16(),
				Checksum = reader.ReadUInt16(),
				InitialIP = reader.ReadUInt16(),
				InitialCS = reader.ReadUInt16(),
				RelocationTableOffset = reader.ReadUInt16(),
				OverlayNumber = reader.ReadUInt16()
			};

			// reserved words
			for (var i = 0; i < 4; i++)
				reader.ReadUInt16();

			header.OemID = reader.ReadUInt16();
			header.OemInfo = reader.ReadUInt16();

			// reserved words
			for (var i = 0; i < 10; i++)
				reader.ReadUInt16();

			header.CoffHeaderOffset = reader.ReadUInt32();

			return header;
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write(DOSSignature);
			writer.Write(BytesOnLastPage);
			writer.Write(PageCount);
			writer.Write(RelocationCount);
			writer.Write(HeaderSize);
			writer.Write(MinExtraParagraphs);
			writer.Write(MaxExtraParagraphs);
			writer.Write(InitialSS);
			writer.Write(InitialSP);
			writer.Write(Checksum);
			writer.Write(InitialIP);
			writer.Write(InitialCS);
			writer.Write(RelocationTableOffset);
			writer.Write(OverlayNumber);

			// reserved words
			for (var i = 0; i < 4; i++)
				writer.Write((ushort)0);
			writer.Write(OemID);
			writer.Write(OemInfo);

			// reserved words
			for (var i = 0; i < 10; i++)
				writer.Write((ushort)0);
			writer.Write(CoffHeaderOffset);
		}
	}
}