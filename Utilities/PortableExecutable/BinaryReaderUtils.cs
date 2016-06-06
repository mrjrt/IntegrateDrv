// Based on work by jachymko, Dec 24, 2006
// Adapted by Tal Aloni, 2011.09.09

using System;
using System.IO;
using System.Text;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public static class BinaryReaderUtils
	{
		public static string ReadFixedLengthAsciiString(BinaryReader reader, int fixedSize)
		{
			var buffer = reader.ReadBytes(fixedSize);
			int len;

			for (len = 0; len < fixedSize; len++)
				if (buffer[len] == 0) break;

			return len > 0
				? Encoding.ASCII.GetString(buffer, 0, len)
				: string.Empty;
		}

		public static string ReadNullTerminatedAsciiString(BinaryReader reader)
		{
			using (var ms = new MemoryStream())
			{
				Byte lastByte;

				do
				{
					lastByte = reader.ReadByte();
					ms.WriteByte(lastByte);
				}
				while ((lastByte > 0) && (reader.BaseStream.Position < reader.BaseStream.Length));

				return Encoding.ASCII.GetString(ms.GetBuffer(), 0, (Int32)(ms.Length - 1));
			}
		}
	}
}
