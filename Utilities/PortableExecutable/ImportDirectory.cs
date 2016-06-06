// Based on work by jachymko, Dec 24, 2006
// Adapted by Tal Aloni, 2011.09.09

using System.Collections.Generic;
using System.IO;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public class ImportDirectory
	{
		public readonly List<ImageImportDescriptor> Descriptors = new List<ImageImportDescriptor>();

		public static ImportDirectory Parse(BinaryReader reader)
		{
			var importDir = new ImportDirectory();
			var desc = ImageImportDescriptor.Parse(reader);
			while (desc.NameRVA != 0)
			{
				importDir.Descriptors.Add(desc);
				desc = ImageImportDescriptor.Parse(reader);
			}
			
			return importDir;
		}

		public void Write(BinaryWriter writer)
		{
			foreach (var descriptor in Descriptors)
				descriptor.Write(writer);
			new ImageImportDescriptor().Write(writer);
		}
	}

	public class ImageImportDescriptor
	{
		// ReSharper disable MemberCanBePrivate.Global
		public uint ImportLookupTableRVA;
		public uint TimeDateStamp;
		public uint ForwardChain;
		public uint NameRVA;
		public uint ImportAddressTableRVA;
		// ReSharper restore MemberCanBePrivate.Global

		public static ImageImportDescriptor Parse(BinaryReader reader)
		{
			var descriptor = new ImageImportDescriptor();
			descriptor.ImportLookupTableRVA = reader.ReadUInt32();
			descriptor.TimeDateStamp = reader.ReadUInt32();
			descriptor.ForwardChain = reader.ReadUInt32();
			descriptor.NameRVA = reader.ReadUInt32();
			descriptor.ImportAddressTableRVA = reader.ReadUInt32();
			return descriptor;
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write(ImportLookupTableRVA);
			writer.Write(TimeDateStamp);
			writer.Write(ForwardChain);
			writer.Write(NameRVA);
			writer.Write(ImportAddressTableRVA);
		}
	}
}
