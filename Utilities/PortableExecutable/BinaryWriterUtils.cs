using System.IO;
using System.Text;

namespace IntegrateDrv.Utilities.PortableExecutable
{
	public static class BinaryWriterUtils
	{
		public static void WriteFixedLengthAsciiString(BinaryWriter writer, string str, int fixedSize)
		{
			if (str.Length > fixedSize)
				str = str.Substring(0, fixedSize);
			var buffer = Encoding.ASCII.GetBytes(str);

			writer.Write(buffer);
			var bytesWritten = buffer.Length;
			while (bytesWritten < fixedSize)
			{
				writer.Write((byte)0);
				bytesWritten++;
			}
		}
	}
}
